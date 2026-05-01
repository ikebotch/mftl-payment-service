using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class GoCardlessPaymentProvider(
    HttpClient httpClient,
    IOptions<GoCardlessSettings> options,
    ILogger<GoCardlessPaymentProvider> logger) : IPaymentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly GoCardlessSettings _settings = options.Value;

    public PaymentProviderType Provider => PaymentProviderType.GoCardless;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        if (!_settings.Enabled)
            return new CreatePaymentResult { Succeeded = false, FailureReason = "GoCardless provider is disabled." };

        if (string.IsNullOrWhiteSpace(_settings.AccessToken))
            return new CreatePaymentResult { Succeeded = false, FailureReason = "GoCardless access token is not configured." };

        var currency = request.Currency.Trim().ToUpperInvariant();
        if (currency is not ("GBP" or "EUR"))
            return new CreatePaymentResult { Succeeded = false, FailureReason = "GoCardless currently supports GBP and EUR for this flow." };

        var billingRequest = await CreateBillingRequestAsync(request, currency, ct);
        if (!billingRequest.Succeeded || string.IsNullOrWhiteSpace(billingRequest.ProviderReference))
            return billingRequest;

        var flow = await CreateBillingRequestFlowAsync(billingRequest.ProviderReference, request, ct);
        if (!flow.Succeeded)
            return flow;

        return new CreatePaymentResult
        {
            Succeeded = true,
            ProviderReference = billingRequest.ProviderReference,
            ProviderTransactionId = billingRequest.ProviderTransactionId,
            CheckoutUrl = flow.CheckoutUrl
        };
    }

    public async Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"/billing_requests/{Uri.EscapeDataString(providerReference)}");
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new VerifyPaymentResult { Succeeded = false, Status = PaymentStatus.Failed, FailureReason = body };

        using var document = JsonDocument.Parse(body);
        var billingRequest = document.RootElement.TryGetProperty("billing_requests", out var root)
            ? root
            : document.RootElement;
        var status = (WebhookHelpers.ReadString(billingRequest, "status") ?? string.Empty).ToLowerInvariant();

        return new VerifyPaymentResult
        {
            Succeeded = true,
            Status = status switch
            {
                "fulfilled" => PaymentStatus.Pending,
                "cancelled" => PaymentStatus.Cancelled,
                "failed" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            },
            ProviderReference = WebhookHelpers.ReadString(billingRequest, "id") ?? providerReference,
            ProviderTransactionId = WebhookHelpers.ReadString(billingRequest, "links", "payment"),
            RawStatus = status
        };
    }

    public async Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var (rawBody, payload) = await WebhookHelpers.ReadBodyAsync(request, ct);
        var payloadHash = WebhookHelpers.ComputeSha256(rawBody);
        var signature = request.Headers["Webhook-Signature"].ToString();
        var expected = string.IsNullOrWhiteSpace(_settings.WebhookSecret)
            ? string.Empty
            : WebhookHelpers.ComputeHmacSha256Hex(_settings.WebhookSecret, rawBody);

        if (!WebhookHelpers.FixedTimeEquals(signature, expected))
        {
            return new WebhookParseResult
            {
                SignatureValid = false,
                FailureReason = "Invalid GoCardless signature.",
                PayloadHash = payloadHash,
                Payload = payload
            };
        }

        var paymentEvent = SelectPaymentEvent(payload);
        if (paymentEvent.ValueKind == JsonValueKind.Undefined)
        {
            return new WebhookParseResult
            {
                SignatureValid = true,
                EventType = "ignored",
                PayloadHash = payloadHash,
                Payload = payload
            };
        }

        var links = paymentEvent.TryGetProperty("links", out var eventLinks) ? eventLinks : default;
        var action = (WebhookHelpers.ReadString(paymentEvent, "action") ?? string.Empty).ToLowerInvariant();
        var resourceType = (WebhookHelpers.ReadString(paymentEvent, "resource_type") ?? string.Empty).ToLowerInvariant();
        var providerReference = ReadLink(links, "billing_request")
            ?? WebhookHelpers.ReadString(paymentEvent, "metadata", "externalReference")
            ?? WebhookHelpers.ReadString(paymentEvent, "metadata", "external_reference");
        var providerTransactionId = ReadLink(links, "payment");

        return new WebhookParseResult
        {
            SignatureValid = true,
            EventId = WebhookHelpers.ReadString(paymentEvent, "id"),
            EventType = string.IsNullOrWhiteSpace(resourceType) ? action : $"{resourceType}.{action}",
            ProviderReference = providerReference,
            ProviderTransactionId = providerTransactionId,
            Status = MapStatus(resourceType, action),
            Amount = ReadMinorUnitAmount(paymentEvent),
            Currency = WebhookHelpers.ReadString(paymentEvent, "currency")?.ToUpperInvariant(),
            PayloadHash = payloadHash,
            Payload = payload,
            FailureReason = WebhookHelpers.ReadString(paymentEvent, "details", "description")
        };
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult
        {
            Succeeded = false,
            Status = PaymentStatus.Failed,
            FailureReason = "TODO: GoCardless refund support is not implemented yet."
        });

    private async Task<CreatePaymentResult> CreateBillingRequestAsync(CreateProviderPaymentRequest request, string currency, CancellationToken ct)
    {
        var payload = new
        {
            billing_requests = new
            {
                payment_request = new
                {
                    description = BuildDescription(request),
                    amount = ConvertToMinorUnits(request.Amount),
                    currency,
                    reference = request.ExternalReference,
                    metadata = BuildMetadata(request)
                }
            }
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "/billing_requests");
        httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GoCardless billing request creation failed. StatusCode={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new CreatePaymentResult { Succeeded = false, FailureReason = body };
        }

        using var document = JsonDocument.Parse(body);
        var billingRequest = document.RootElement.GetProperty("billing_requests");

        return new CreatePaymentResult
        {
            Succeeded = true,
            ProviderReference = WebhookHelpers.ReadString(billingRequest, "id"),
            ProviderTransactionId = WebhookHelpers.ReadString(billingRequest, "links", "payment")
        };
    }

    private async Task<CreatePaymentResult> CreateBillingRequestFlowAsync(string billingRequestId, CreateProviderPaymentRequest paymentRequest, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["billing_request_flows"] = BuildFlowPayload(billingRequestId, paymentRequest)
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "/billing_request_flows");
        httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GoCardless billing request flow creation failed. StatusCode={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new CreatePaymentResult { Succeeded = false, FailureReason = body };
        }

        using var document = JsonDocument.Parse(body);
        var flow = document.RootElement.GetProperty("billing_request_flows");

        return new CreatePaymentResult
        {
            Succeeded = true,
            CheckoutUrl = WebhookHelpers.ReadString(flow, "authorisation_url")
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_settings.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.AccessToken);
        request.Headers.TryAddWithoutValidation("GoCardless-Version", _settings.ApiVersion);
        return request;
    }

    private Dictionary<string, object?> BuildFlowPayload(string billingRequestId, CreateProviderPaymentRequest request)
    {
        var flow = new Dictionary<string, object?>
        {
            ["redirect_uri"] = BuildRedirectUri(request.ExternalReference),
            ["links"] = new Dictionary<string, string>
            {
                ["billing_request"] = billingRequestId
            }
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            flow["prefilled_customer"] = new Dictionary<string, string>
            {
                ["email"] = request.CustomerEmail.Trim()
            };
        }

        return flow;
    }

    private string BuildRedirectUri(string externalReference)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.RedirectBaseUrl)
            ? "https://example.com/payments/gocardless/return"
            : _settings.RedirectBaseUrl.TrimEnd('/');

        return $"{baseUrl}?externalReference={Uri.EscapeDataString(externalReference)}";
    }

    private static Dictionary<string, string> BuildMetadata(CreateProviderPaymentRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["externalReference"] = request.ExternalReference
        };

        AddMetadataValue(metadata, "tenantId", request.Metadata);
        AddMetadataValue(metadata, "contributionId", request.Metadata);
        return metadata;
    }

    private static void AddMetadataValue(Dictionary<string, string> metadata, string key, JsonElement source)
    {
        if (metadata.Count >= 3 || source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value))
            return;

        var raw = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        if (!string.IsNullOrWhiteSpace(raw))
            metadata[key] = raw;
    }

    private static string BuildDescription(CreateProviderPaymentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Description))
            return Truncate(request.Description.Trim(), 255);

        return Truncate($"MFTL contribution {request.ExternalReference}", 255);
    }

    private static JsonElement SelectPaymentEvent(JsonElement payload)
    {
        if (!payload.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            return default;

        foreach (var evt in events.EnumerateArray())
        {
            var resourceType = (WebhookHelpers.ReadString(evt, "resource_type") ?? string.Empty).ToLowerInvariant();
            if (resourceType is "payments" or "billing_requests")
                return evt.Clone();
        }

        return default;
    }

    private static PaymentStatus? MapStatus(string resourceType, string action)
    {
        return (resourceType, action) switch
        {
            ("payments", "confirmed") => PaymentStatus.Succeeded,
            ("payments", "paid_out") => PaymentStatus.Succeeded,
            ("payments", "failed") => PaymentStatus.Failed,
            ("payments", "cancelled") => PaymentStatus.Cancelled,
            ("billing_requests", "cancelled") => PaymentStatus.Cancelled,
            ("billing_requests", "failed") => PaymentStatus.Failed,
            _ => null
        };
    }

    private static string? ReadLink(JsonElement links, string name)
    {
        if (links.ValueKind != JsonValueKind.Object)
            return null;

        return WebhookHelpers.ReadString(links, name);
    }

    private static decimal? ReadMinorUnitAmount(JsonElement element)
    {
        var raw = WebhookHelpers.ReadDecimal(element, "amount");
        return raw.HasValue ? raw.Value / 100m : null;
    }

    private static int ConvertToMinorUnits(decimal amount) =>
        (int)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
