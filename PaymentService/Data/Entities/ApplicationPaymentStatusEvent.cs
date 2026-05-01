namespace MftlPaymentService.Data.Entities;

public sealed class ApplicationPaymentStatusEvent
{
    public long Id { get; set; }
    public Guid ApplicationPaymentId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string RawStatus { get; set; } = string.Empty;
    public string NormalizedStatus { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; set; }

    public ApplicationPayment ApplicationPayment { get; set; } = null!;
}
