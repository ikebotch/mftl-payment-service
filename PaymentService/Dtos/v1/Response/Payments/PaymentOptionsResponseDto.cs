namespace MftlPaymentService.Dtos.v1.Response.Payments;

public sealed record PaymentOptionsResponseDto(
    IReadOnlyList<PaymentOptionDto> Methods
);
