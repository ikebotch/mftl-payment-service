namespace MftlPaymentService.Dtos.v1.Response.Payments;

public sealed record CreateApplicationPaymentResponseDto(
    string PaymentReference,
    string Status,
    bool Verified,
    string? ReceiptUrl
);
