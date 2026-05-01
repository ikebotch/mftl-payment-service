namespace MftlPaymentService.Domain;

public enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Refunded = 5
}
