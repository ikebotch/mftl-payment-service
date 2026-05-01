using MftlPaymentService.Domain;

namespace MftlPaymentService.Contracts.Payments;

public sealed record CreatePaymentResponseDto(
    Guid PaymentId,
    PaymentStatus Status,
    PaymentProviderType Provider,
    string? ProviderReference,
    string? CheckoutUrl,
    string ExternalReference);
