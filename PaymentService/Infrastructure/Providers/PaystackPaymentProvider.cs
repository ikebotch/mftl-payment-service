using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using MftlPaymentService.Domain;
using MftlPaymentService.Settings;
using Microsoft.Extensions.Options;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class PaystackPaymentProvider(
    HttpClient httpClient,
    IOptions<PaystackSettings> options,
    ILogger<PaystackPaymentProvider> logger) : IPaymentProvider
{
    private readonly PaystackSettings _settings = options.Value;
    public PaymentProviderType Provider => PaymentProviderType.Paystack;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_settings.BaseUrl.TrimEnd('/')}/transaction/initialize");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SecretKey);

        var payload = new
        {
            email = string.IsNullOrWhiteSpace(request.CustomerEmail) ? BuildFallbackEmail(request.CustomerPhone) : request.CustomerEmail,
            amount = ConvertToMinorUnits(request.Amount),
            currency = (request.Currency ?? _settings.DefaultCurrency).ToUpperInvariant(),
            reference = request.ExternalReference,
            callback_url = _settings.CallbackUrl,
            metadata = new Dictionary<string, object?>
            {
                ["clientApp"] = request.ClientApp,
                ["externalReference"] = request.ExternalReference,
                ["customerPhone"] = request.CustomerPhone,
                ["description"] = request.Description
            }
        };

        httpRequest.Content = JsonContent.Create(payload);
        using var response = await httpClient.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Paystack initialize failed. StatusCode={StatusCode} Body={Body}", (int)response.StatusCode, body);
            return new CreatePaymentResult { Succeeded = false, FailureReason = body };
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var data = root.GetProperty("data");

        return new CreatePaymentResult
        {
            Succeeded = true,
            ProviderReference = WebhookHelpers.ReadString(data, "reference"),
            CheckoutUrl = WebhookHelpers.ReadString(data, "authorization_url")
        };
    }

    public async Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.BaseUrl.TrimEnd('/')}/transaction/verify/{Uri.EscapeDataString(providerReference)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.SecretKey);
        using var response = await httpClient.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new VerifyPaymentResult { Succeeded = false, Status = PaymentStatus.Failed, FailureReason = body };

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        var rawStatus = (WebhookHelpers.ReadString(data, "status") ?? string.Empty).ToLowerInvariant();

        return new VerifyPaymentResult
        {
            Succeeded = true,
            Status = rawStatus switch
            {
                "success" => PaymentStatus.Succeeded,
                "abandoned" or "failed" or "reversed" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            },
            ProviderReference = providerReference,
            ProviderTransactionId = WebhookHelpers.ReadString(data, "id"),
            RawStatus = rawStatus
        };
    }

    public async Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var (rawBody, payload) = await WebhookHelpers.ReadBodyAsync(request, ct);
        var signature = request.Headers["x-paystack-signature"].ToString();
        var expected = WebhookHelpers.ComputeHmacSha512Hex(_settings.WebhookSecretOrSecretKey, rawBody);

        if (!WebhookHelpers.FixedTimeEquals(signature, expected))
        {
            return new WebhookParseResult
            {
                SignatureValid = false,
                FailureReason = "Invalid Paystack signature.",
                PayloadHash = WebhookHelpers.ComputeSha256(rawBody),
                Payload = payload
            };
        }

        var eventName = WebhookHelpers.ReadString(payload, "event");
        var data = payload.TryGetProperty("data", out var rootData) ? rootData : payload;
        var rawStatus = (WebhookHelpers.ReadString(data, "status") ?? string.Empty).ToLowerInvariant();

        return new WebhookParseResult
        {
            SignatureValid = true,
            EventId = WebhookHelpers.ReadString(data, "id") ?? $"{eventName}:{WebhookHelpers.ReadString(data, "reference")}",
            EventType = eventName,
            ProviderReference = WebhookHelpers.ReadString(data, "reference"),
            ProviderTransactionId = WebhookHelpers.ReadString(data, "id"),
            Status = eventName switch
            {
                "charge.success" => PaymentStatus.Succeeded,
                "charge.failed" => PaymentStatus.Failed,
                "transfer.reversed" => PaymentStatus.Refunded,
                _ => rawStatus switch
                {
                    "success" => PaymentStatus.Succeeded,
                    "abandoned" or "failed" => PaymentStatus.Failed,
                    _ => null
                }
            },
            PayloadHash = WebhookHelpers.ComputeSha256(rawBody),
            Payload = payload
        };
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult
        {
            Succeeded = false,
            Status = PaymentStatus.Failed,
            FailureReason = "TODO: Paystack refund support is not implemented yet."
        });

    private static string BuildFallbackEmail(string? phone)
    {
        var digits = string.IsNullOrWhiteSpace(phone) ? string.Empty : new string(phone.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? "customer@mftl.local" : $"{digits}@mftl.local";
    }

    private static int ConvertToMinorUnits(decimal amount) => (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
