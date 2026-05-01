using MftlPaymentService.Domain;
using System.Text.Json;

namespace MftlPaymentService.Contracts.Payments;

public sealed class ClientCallbackPayload
{
    public string CallbackEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Guid PaymentServicePaymentId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? ContributionId { get; set; }
    public PaymentProviderType Provider { get; set; }
    public string? ProviderReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public string ClientApp { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public PaymentStatus Status { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public JsonElement Metadata { get; set; }
}
