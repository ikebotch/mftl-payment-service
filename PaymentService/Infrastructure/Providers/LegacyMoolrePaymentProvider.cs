using MftlPaymentService.Domain;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Providers.v1;
using System.Text.Json;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class LegacyMoolrePaymentProvider(IMoolreProvider provider) : IPaymentProvider
{
    public PaymentProviderType Provider => PaymentProviderType.Moolre;

    public async Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct)
    {
        var response = await provider.InitiateCollection(new InitiateCollectionRequestDto
        {
            Amount = request.Amount,
            Currency = request.Currency,
            PhoneNumber = request.CustomerPhone ?? string.Empty,
            Reference = request.ExternalReference,
            UserReference = request.ExternalReference,
            Network = "moolre"
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
        var txStatus = WebhookHelpers.ReadString(data, "TxStatus");

        return new WebhookParseResult
        {
            SignatureValid = true,
            EventId = WebhookHelpers.ReadString(data, "TransactionId") ?? Guid.NewGuid().ToString("N"),
            EventType = "moolre.transaction",
            ProviderReference = WebhookHelpers.ReadString(data, "ExternalRef") ?? WebhookHelpers.ReadString(data, "ThirdPartyRef"),
            ProviderTransactionId = WebhookHelpers.ReadString(data, "TransactionId"),
            Status = txStatus switch
            {
                "1" => PaymentStatus.Succeeded,
                "2" => PaymentStatus.Failed,
                _ => PaymentStatus.Pending
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
            FailureReason = "TODO: Moolre refund support is not implemented."
        });
}
