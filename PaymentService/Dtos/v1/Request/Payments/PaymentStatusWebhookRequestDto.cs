using System.ComponentModel.DataAnnotations;

namespace MftlPaymentService.Dtos.v1.Request.Payments;

public sealed class PaymentStatusWebhookRequestDto
{
    [Required] public string Id { get; set; } = string.Empty;
    [Required] public string PaymentId { get; set; } = string.Empty;
    [Required] public string Status { get; set; } = string.Empty;
}
