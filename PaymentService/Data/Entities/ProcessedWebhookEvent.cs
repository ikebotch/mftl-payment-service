using MftlPaymentService.Domain;

namespace MftlPaymentService.Data.Entities;

public sealed class ProcessedWebhookEvent
{
    public Guid Id { get; set; }
    public Guid? PaymentRecordId { get; set; }
    public PaymentProviderType Provider { get; set; }
    public string? EventId { get; set; }
    public string? ProviderReference { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public WebhookProcessingStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }

    public PaymentRecord? PaymentRecord { get; set; }
}
