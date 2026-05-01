namespace MftlPaymentService.Contracts.Payments;

public sealed class RefundPaymentRequestDto
{
    public decimal? Amount { get; set; }
    public string? Reason { get; set; }
}
