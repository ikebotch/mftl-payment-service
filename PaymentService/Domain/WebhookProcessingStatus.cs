namespace MftlPaymentService.Domain;

public enum WebhookProcessingStatus
{
    Pending = 1,
    Processed = 2,
    IgnoredDuplicate = 3,
    Rejected = 4,
    Failed = 5
}
