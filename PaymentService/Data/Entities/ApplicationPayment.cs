namespace MftlPaymentService.Data.Entities;

public sealed class ApplicationPayment
{
    public Guid Id { get; set; }
    public Guid ApplicationId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string PaymentOptionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public string Status { get; set; } = "pending";
    public bool Verified { get; set; }
    public string? ProviderTransactionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<ApplicationPaymentStatusEvent> StatusEvents { get; set; } = new List<ApplicationPaymentStatusEvent>();
}
