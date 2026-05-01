namespace MftlPaymentService.Dtos.v1.Response.Payments;

public sealed record ApplicationPaymentSummaryResponseDto(
    string ServiceName,
    decimal ServiceCost,
    string Currency,
    bool CanPay,
    bool AlreadyPaid,
    string? PaymentReference
);
