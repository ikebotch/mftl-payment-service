using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MftlPaymentService.Domain;
using MftlPaymentService.Settings;
using Microsoft.Extensions.Options;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class StripePaymentProvider(
    HttpClient httpClient,
    IOptions<StripeSettings> options,
    ILogger<StripePaymentProvider> logger) : IPaymentProvider
{
    private readonly StripeSettings _settings = options.Value;
    public PaymentProviderType Provider => PaymentProviderType.Stripe;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/v1/checkout/sessions");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SecretKey);
        var payload = new List<KeyValuePair<string, string>>
        {
            new("mode", "payment"),
            new("success_url", _settings.SuccessUrl),
            new("cancel_url", _settings.CancelUrl),
            new("line_items[0][price_data][currency]", (request.Currency ?? _settings.DefaultCurrency).ToLowerInvariant()),
            new("line_items[0][price_data][unit_amount]", ConvertToMinorUnits(request.Amount).ToString(CultureInfo.InvariantCulture)),
            new("line_items[0][price_data][product_data][name]", request.Description ?? $"Payment for {request.ClientApp}"),
            new("line_items[0][quantity]", "1"),
            new("client_reference_id", request.ExternalReference),
            new("metadata[clientApp]", request.ClientApp),
            new("metadata[externalReference]", request.ExternalReference)
        };

        if (!string.IsNullOrWhiteSpace(request.CustomerEmail))
            payload.Add(new("customer_email", request.CustomerEmail));

        foreach (var pair in FlattenMetadata(request.Metadata))
            payload.Add(new($"metadata[{pair.Key}]", pair.Value));

        httpRequest.Content = new FormUrlEncodedContent(payload);

        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Stripe create payment failed. StatusCode={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new CreatePaymentResult { Succeeded = false, FailureReason = body };
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        return new CreatePaymentResult
        {
            Succeeded = true,
            ProviderReference = WebhookHelpers.ReadString(root, "id"),
            ProviderTransactionId = WebhookHelpers.ReadString(root, "payment_intent"),
            CheckoutUrl = WebhookHelpers.ReadString(root, "url")
        };
    }

    public async Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl.TrimEnd('/')}/v1/checkout/sessions/{Uri.EscapeDataString(providerReference)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SecretKey);

        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new VerifyPaymentResult { Succeeded = false, Status = PaymentStatus.Failed, FailureReason = body };

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var paymentStatus = (WebhookHelpers.ReadString(root, "payment_status") ?? string.Empty).ToLowerInvariant();
        var status = paymentStatus switch
        {
            "paid" => PaymentStatus.Succeeded,
            "unpaid" => PaymentStatus.Pending,
            "no_payment_required" => PaymentStatus.Succeeded,
            _ => PaymentStatus.Pending
        };

        return new VerifyPaymentResult
        {
            Succeeded = true,
            Status = status,
            ProviderReference = WebhookHelpers.ReadString(root, "id"),
            ProviderTransactionId = WebhookHelpers.ReadString(root, "payment_intent"),
            RawStatus = paymentStatus
        };
    }

    public async Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var (rawBody, payload) = await WebhookHelpers.ReadBodyAsync(request, ct);
        var signatureHeader = request.Headers["Stripe-Signature"].ToString();
        if (!TryValidateSignature(signatureHeader, rawBody))
        {
            return new WebhookParseResult
            {
                SignatureValid = false,
                FailureReason = "Invalid Stripe signature.",
                PayloadHash = WebhookHelpers.ComputeSha256(rawBody),
                Payload = payload
            };
        }

        var type = WebhookHelpers.ReadString(payload, "type");
        var dataObject = payload.TryGetProperty("data", out var data) && data.TryGetProperty("object", out var obj)
            ? obj
            : payload;

        return new WebhookParseResult
        {
            SignatureValid = true,
            EventId = WebhookHelpers.ReadString(payload, "id"),
            EventType = type,
            ProviderReference = WebhookHelpers.ReadString(dataObject, "id"),
            ProviderTransactionId = WebhookHelpers.ReadString(dataObject, "payment_intent"),
            Status = MapStripeStatus(type, dataObject),
            PayloadHash = WebhookHelpers.ComputeSha256(rawBody),
            Payload = payload
        };
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult
        {
            Succeeded = false,
            Status = PaymentStatus.Failed,
            FailureReason = "TODO: Stripe refund support is not implemented yet."
        });

    private bool TryValidateSignature(string signatureHeader, string rawBody)
    {
        if (string.IsNullOrWhiteSpace(_settings.WebhookSecret) || string.IsNullOrWhiteSpace(signatureHeader))
            return false;

        string? timestamp = null;
        var signatures = new List<string>();
        foreach (var piece in signatureHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = piece.Split('=', 2);
            if (parts.Length != 2)
                continue;

            if (parts[0] == "t")
                timestamp = parts[1];
            if (parts[0] == "v1")
                signatures.Add(parts[1]);
        }

        if (string.IsNullOrWhiteSpace(timestamp) || signatures.Count == 0)
            return false;

        var signedPayload = $"{timestamp}.{rawBody}";
        var expectedSignature = WebhookHelpers.ComputeHmacSha256Hex(_settings.WebhookSecret, signedPayload);
        return signatures.Any(signature => WebhookHelpers.FixedTimeEquals(signature, expectedSignature));
    }

    private static PaymentStatus? MapStripeStatus(string? eventType, JsonElement dataObject)
    {
        return eventType switch
        {
            "checkout.session.completed" => PaymentStatus.Succeeded,
            "checkout.session.async_payment_succeeded" => PaymentStatus.Succeeded,
            "checkout.session.expired" => PaymentStatus.Cancelled,
            "payment_intent.payment_failed" => PaymentStatus.Failed,
            "charge.refunded" => PaymentStatus.Refunded,
            _ => null
        };
    }

    private static IEnumerable<KeyValuePair<string, string>> FlattenMetadata(JsonElement metadata)
    {
        if (metadata.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var property in metadata.EnumerateObject())
        {
            yield return new KeyValuePair<string, string>(property.Name, property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Null => string.Empty,
                _ => property.Value.GetRawText()
            });
        }
    }

    private static int ConvertToMinorUnits(decimal amount) => (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
