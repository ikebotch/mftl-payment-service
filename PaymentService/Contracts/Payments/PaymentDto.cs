using MftlPaymentService.Domain;
using System.Text.Json;

namespace MftlPaymentService.Contracts.Payments;

public sealed class PaymentDto
{
    public Guid Id { get; set; }
    public string ClientApp { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ContributionId { get; set; }
    public string ExternalReference { get; set; } = string.Empty;
    public PaymentProviderType Provider { get; set; }
    public string? ProviderReference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "GHS";
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? Description { get; set; }
    public string? CallbackUrl { get; set; }
    public JsonElement Metadata { get; set; }
    public PaymentStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
