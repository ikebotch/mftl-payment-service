using Microsoft.AspNetCore.Http;
using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Domain;

namespace MftlPaymentService.Services;

public interface IPaymentOrchestrator
{
    Task<CreatePaymentResponseDto> CreatePaymentAsync(CreatePaymentRequestDto request, CancellationToken ct);
    Task<PaymentDto?> GetPaymentAsync(Guid id, CancellationToken ct);
    Task<PaymentDto?> GetPaymentByReferenceAsync(string externalReference, CancellationToken ct);
    Task<PaymentDto> VerifyPaymentAsync(Guid id, CancellationToken ct);
    Task<PaymentDto> RefundPaymentAsync(Guid id, RefundPaymentRequestDto request, CancellationToken ct);
    Task<WebhookProcessOutcome> ProcessWebhookAsync(PaymentProviderType provider, HttpRequest request, CancellationToken ct);
}

public sealed record WebhookProcessOutcome(bool Accepted, bool Duplicate, string Message, PaymentDto? Payment);
