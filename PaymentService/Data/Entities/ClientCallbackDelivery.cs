using MftlPaymentService.Domain;

namespace MftlPaymentService.Data.Entities;

public sealed class ClientCallbackDelivery
{
    public Guid Id { get; set; }
    public Guid PaymentRecordId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string CallbackUrl { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public ClientCallbackStatus Status { get; set; } = ClientCallbackStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }

    public PaymentRecord PaymentRecord { get; set; } = null!;
}
