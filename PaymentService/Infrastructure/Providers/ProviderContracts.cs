using Microsoft.AspNetCore.Http;
using MftlPaymentService.Domain;
using System.Text.Json;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class CreateProviderPaymentRequest
{
    public string ClientApp { get; init; } = string.Empty;
    public string ExternalReference { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "GHS";
    public string? CustomerEmail { get; init; }
    public string? CustomerPhone { get; init; }
    public string? Description { get; init; }
    public JsonElement Metadata { get; init; }
}

public sealed class CreatePaymentResult
{
    public bool Succeeded { get; init; }
    public string? ProviderReference { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? CheckoutUrl { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class VerifyPaymentResult
{
    public bool Succeeded { get; init; }
    public PaymentStatus Status { get; init; }
    public string? ProviderReference { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? RawStatus { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class WebhookParseResult
{
    public bool SignatureValid { get; init; }
    public string? EventId { get; init; }
    public string? ProviderReference { get; init; }
    public string? ProviderTransactionId { get; init; }
    public PaymentStatus? Status { get; init; }
    public string? EventType { get; init; }
    public string PayloadHash { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
    public string? FailureReason { get; init; }
}

public sealed class RefundPaymentRequest
{
    public string ProviderReference { get; init; } = string.Empty;
    public decimal? Amount { get; init; }
    public string? Reason { get; init; }
}

public sealed class RefundResult
{
    public bool Succeeded { get; init; }
    public PaymentStatus Status { get; init; }
    public string? ProviderTransactionId { get; init; }
    public string? FailureReason { get; init; }
}

public interface IPaymentProvider
{
    PaymentProviderType Provider { get; }
    Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct);
    Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct);
    Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct);
    Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct);
}

public interface IPaymentProviderResolver
{
    IPaymentProvider Resolve(PaymentProviderType provider);
}
