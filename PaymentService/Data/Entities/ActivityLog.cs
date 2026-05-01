namespace MftlPaymentService.Data.Entities;

public sealed class ActivityLog
{
    public long Id { get; set; }
    public string Activity { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? Reference { get; set; }
    public Guid? ApplicationId { get; set; }
    public string? PaymentReference { get; set; }
    public string? RequestPayload { get; set; }
    public string? ResponsePayload { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
