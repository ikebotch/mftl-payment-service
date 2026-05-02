using MftlPaymentService.Domain;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Providers.v1;
using System.Text.Json;
using MftlPaymentService.Settings;
using Microsoft.Extensions.Options;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class LegacyMoolrePaymentProvider(
    IMoolreProvider provider,
    IOptions<MoolreSettings> options) : IPaymentProvider
{
    private readonly MoolreSettings _settings = options.Value;
    public PaymentProviderType Provider => PaymentProviderType.Moolre;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        var network = GetMetadataString(request.Metadata, "momoNetwork") ?? "moolre";
        var phoneNumber = GetMetadataString(request.Metadata, "momoPhoneNumber") ?? request.CustomerPhone ?? string.Empty;

        var response = await provider.InitiateCollection(new InitiateCollectionRequestDto
        {
            Amount = request.Amount,
            Currency = request.Currency,
            PhoneNumber = phoneNumber,
            Reference = request.ExternalReference,
            UserReference = request.ExternalReference,
            Network = network
        });

        return new CreatePaymentResult
        {
            Succeeded = string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase),
            ProviderReference = response.Data?.Reference,
            FailureReason = response.Message
        };
    }

    public async Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct)
    {
        var response = await provider.CheckPaymentStatus(providerReference);
        var rawStatus = response.Data?.Status?.ToLowerInvariant();

        return new VerifyPaymentResult
        {
            Succeeded = string.Equals(response.Status, "success", StringComparison.OrdinalIgnoreCase),
            Status = rawStatus switch
            {
                "success" => PaymentStatus.Succeeded,
                "failed" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
            },
            ProviderReference = providerReference,
            RawStatus = rawStatus,
            FailureReason = response.Message
        };
    }

    public async Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct)
    {
        var (rawBody, payload) = await WebhookHelpers.ReadBodyAsync(request, ct);
        var data = payload.TryGetProperty("data", out var parsedData) ? parsedData : payload;
        
        // Moolre verification: they typically send the secret in the payload
        var payloadSecret = WebhookHelpers.ReadString(data, "secret");
        var isRealMode = string.Equals(_settings.Mode, "Real", StringComparison.OrdinalIgnoreCase);
        var signatureValid = true;
        var failureReason = (string?)null;

        if (isRealMode)
        {
            if (string.IsNullOrWhiteSpace(_settings.WebhookSecret))
            {
                signatureValid = false;
                failureReason = "Moolre WebhookSecret is not configured in Real mode.";
            }
            else if (!WebhookHelpers.FixedTimeEquals(payloadSecret, _settings.WebhookSecret))
            {
                signatureValid = false;
                failureReason = "Invalid Moolre webhook secret.";
            }
        }

        var txStatus = WebhookHelpers.ReadString(data, "TxStatus") ?? WebhookHelpers.ReadString(data, "txstatus");
        var providerCode = WebhookHelpers.ReadString(payload, "Code") ?? WebhookHelpers.ReadString(payload, "code");
        
        var status = (txStatus == "1")
            ? PaymentStatus.Succeeded
            : (txStatus == "2" ? PaymentStatus.Failed : PaymentStatus.Pending);

        return new WebhookParseResult
        {
            SignatureValid = signatureValid,
            FailureReason = failureReason,
            EventId = WebhookHelpers.ReadString(data, "TransactionId") ?? WebhookHelpers.ReadString(data, "transactionid") ?? Guid.NewGuid().ToString("N"),
            EventType = "moolre.transaction",
            ProviderReference = WebhookHelpers.ReadString(data, "ExternalRef") ?? WebhookHelpers.ReadString(data, "externalref") ?? WebhookHelpers.ReadString(data, "ThirdPartyRef"),
            ProviderTransactionId = WebhookHelpers.ReadString(data, "TransactionId") ?? WebhookHelpers.ReadString(data, "transactionid"),
            Amount = WebhookHelpers.ReadDecimal(data, "Amount") ?? WebhookHelpers.ReadDecimal(data, "value") ?? WebhookHelpers.ReadDecimal(data, "amount"),
            Currency = WebhookHelpers.ReadString(data, "Currency") ?? WebhookHelpers.ReadString(data, "currency"),
            Status = status,
            PayloadHash = WebhookHelpers.ComputeSha256(rawBody),
            Payload = payload
        };
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) =>
        Task.FromResult(new RefundResult
        {
            Succeeded = false,
            Status = PaymentStatus.Failed,
            FailureReason = "TODO: Moolre refund support is not implemented."
        });

    private static string? GetMetadataString(JsonElement metadata, string key)
    {
        if (metadata.ValueKind != JsonValueKind.Object || !metadata.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            _ => value.ToString()
        };
    }
}
