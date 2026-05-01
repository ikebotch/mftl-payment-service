namespace MftlPaymentService.Dtos.v1.Response.Payments;

public sealed record PaymentOptionDto(
    string Id,
    string Name,
    string? IconUrl,
    string Description
);
