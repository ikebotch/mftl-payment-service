using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class MolliePaymentProvider(
    HttpClient httpClient,
    IOptions<MollieSettings> options,
    ILogger<MolliePaymentProvider> logger) : IPaymentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MollieSettings _settings = options.Value;

    public PaymentProviderType Provider => PaymentProviderType.Mollie;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        if (!_settings.Enabled)
            return new CreatePaymentResult { Succeeded = false, FailureReason = "Mollie provider is disabled." };

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            return new CreatePaymentResult { Succeeded = false, FailureReason = "Mollie API key is not configured." };

        var webhookUrl = BuildWebhookUrl();
        if (IsLiveEnvironment() && IsLocalhostUrl(webhookUrl))
            return new CreatePaymentResult { Succeeded = false, FailureReason = "Mollie webhook URL must be public in Live environment." };

        var payload = new
        {
            amount = new
            {
                currency = request.Currency.Trim().ToUpperInvariant(),
                value = request.Amount.ToString("0.00", CultureInfo.InvariantCulture)
            },
            method = "creditcard",
            description = BuildDescription(request),
            redirectUrl = BuildRedirectUrl(request.ExternalReference),
            webhookUrl,
            metadata = BuildMetadata(request)
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "/v2/payments");
        httpRequest.Content = JsonContent.Create(payload, options: JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Mollie payment creation failed. StatusCode={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new CreatePaymentResult { Succeeded = false, FailureReason = body };
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        return new CreatePaymentResult
        {
            Succeeded = true,
            ProviderReference = WebhookHelpers.ReadString(root, "id"),
            CheckoutUrl = WebhookHelpers.ReadString(root, "_links", "checkout", "href")
        };
    }

    public async Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct)
    {
        var fetched = await FetchPaymentAsync(providerReference, ct);
        return fetched.ParseResult.SignatureValid
            ? new VerifyPaymentResult
            {
                Succeeded = true,
                Status = fetched.ParseResult.Status ?? PaymentStatus.Pending,
                ProviderReference = fetched.ParseResult.ProviderReference,
                ProviderTransactionId = fetched.ParseResult.ProviderTransactionId,
                RawStatus = fetched.RawStatus,
                FailureReason = fetched.ParseResult.FailureReason
            }
            : new VerifyPaymentResult
            {
                Succeeded = false,
                Status = PaymentStatus.Failed,
                FailureReason = fetched.ParseResult.FailureReason
            };
    }

    public async Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var paymentId = await ReadWebhookPaymentIdAsync(request, ct);
        if (string.IsNullOrWhiteSpace(paymentId))
        {
            return new WebhookParseResult
            {
                SignatureValid = false,
                FailureReason = "Missing Mollie payment id.",
                PayloadHash = string.Empty,
                Payload = JsonSerializer.SerializeToElement(new { })
            };
        }

        return (await FetchPaymentAsync(paymentId, ct)).ParseResult;
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult
        {
            Succeeded = false,
            Status = PaymentStatus.Failed,
            FailureReason = "TODO: Mollie refund support is not implemented yet."
        });

    private async Task<(WebhookParseResult ParseResult, string? RawStatus)> FetchPaymentAsync(string paymentId, CancellationToken ct)
    {
        using var httpRequest = CreateRequest(HttpMethod.Get, $"/v2/payments/{Uri.EscapeDataString(paymentId)}");
        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        var payloadHash = WebhookHelpers.ComputeSha256(body);

        if (!response.IsSuccessStatusCode)
        {
            return (new WebhookParseResult
            {
                SignatureValid = false,
                FailureReason = $"Mollie payment fetch failed: {(int)response.StatusCode}.",
                EventId = paymentId,
                ProviderReference = paymentId,
                PayloadHash = payloadHash,
                Payload = JsonSerializer.SerializeToElement(new { id = paymentId, fetchStatus = (int)response.StatusCode })
            }, null);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement.Clone();
        var status = (WebhookHelpers.ReadString(root, "status") ?? string.Empty).ToLowerInvariant();
        var providerReference = WebhookHelpers.ReadString(root, "id") ?? paymentId;

        return (new WebhookParseResult
        {
            SignatureValid = true,
            EventId = $"{providerReference}:{status}",
            EventType = $"payment.{status}",
            ProviderReference = providerReference,
            ProviderTransactionId = providerReference,
            Status = MapStatus(status),
            Amount = WebhookHelpers.ReadDecimal(root, "amount", "value"),
            Currency = WebhookHelpers.ReadString(root, "amount", "currency")?.ToUpperInvariant(),
            PayloadHash = payloadHash,
            Payload = root,
            FailureReason = status is "failed" or "canceled" or "expired" ? $"Mollie payment {status}." : null
        }, status);
    }

    private static async Task<string?> ReadWebhookPaymentIdAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            request.Body.Position = 0;
            return form["id"].FirstOrDefault();
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;

        if (string.IsNullOrWhiteSpace(rawBody))
            return null;

        try
        {
            using var document = JsonDocument.Parse(rawBody);
            return WebhookHelpers.ReadString(document.RootElement, "id")
                ?? WebhookHelpers.ReadString(document.RootElement, "paymentId");
        }
        catch (JsonException)
        {
            var pairs = rawBody.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "id", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parts[1].Replace("+", " "));
            }
        }

        return null;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{_settings.BaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        return request;
    }

    private string BuildRedirectUrl(string externalReference)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.RedirectBaseUrl)
            ? "https://example.com/payments/mollie/return"
            : _settings.RedirectBaseUrl.TrimEnd('/');

        return $"{baseUrl}?externalReference={Uri.EscapeDataString(externalReference)}";
    }

    private string BuildWebhookUrl()
    {
        if (Uri.TryCreate(_settings.WebhookPath, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var baseUrl = string.IsNullOrWhiteSpace(_settings.WebhookBaseUrl)
            ? _settings.RedirectBaseUrl
            : _settings.WebhookBaseUrl;

        if (string.IsNullOrWhiteSpace(baseUrl))
            return $"https://example.com{_settings.WebhookPath}";

        return $"{baseUrl.TrimEnd('/')}/{_settings.WebhookPath.TrimStart('/')}";
    }

    private bool IsLiveEnvironment() =>
        string.Equals(_settings.Environment, "Live", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalhostUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildMetadata(CreateProviderPaymentRequest request)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["clientApp"] = request.ClientApp,
            ["externalReference"] = request.ExternalReference
        };

        AddMetadataValue(metadata, "tenantId", request.Metadata);
        AddMetadataValue(metadata, "contributionId", request.Metadata);
        return metadata;
    }

    private static void AddMetadataValue(Dictionary<string, string> metadata, string key, JsonElement source)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty(key, out var value))
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

    private static PaymentStatus? MapStatus(string status) => status switch
    {
        "paid" => PaymentStatus.Succeeded,
        "failed" => PaymentStatus.Failed,
        "canceled" => PaymentStatus.Cancelled,
        "expired" => PaymentStatus.Cancelled,
        "open" or "pending" or "authorized" => PaymentStatus.Pending,
        _ => null
    };

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
