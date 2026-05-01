namespace MftlPaymentService.Domain;

public enum ClientCallbackStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    DeadLetter = 4
}
