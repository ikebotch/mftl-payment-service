namespace MftlPaymentService.Dtos.v1.Response.Payments;

public sealed record PaymentStatusWebhookResponseDto(
    string Status,
    string Message,
    string Id,
    string PaymentId,
    string PaymentStatus,
    bool Verified,
    DateTimeOffset ProcessedAtUtc
);
