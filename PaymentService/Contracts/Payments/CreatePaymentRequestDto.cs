using MftlPaymentService.Domain;
using System.Text.Json;

namespace MftlPaymentService.Contracts.Payments;

public sealed class CreatePaymentRequestDto
{
    public string ClientApp { get; set; } = string.Empty;
    public string ExternalReference { get; set; } = string.Empty;
    public PaymentProviderType Provider { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerEmail { get; set; }
    public string? CustomerPhone { get; set; }
    public string? Description { get; set; }
    public string? CallbackUrl { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? ContributionId { get; set; }
    public JsonElement? Metadata { get; set; }
}
