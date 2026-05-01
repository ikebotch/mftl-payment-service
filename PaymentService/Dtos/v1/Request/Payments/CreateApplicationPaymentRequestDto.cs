using System.ComponentModel.DataAnnotations;

namespace MftlPaymentService.Dtos.v1.Request.Payments;

public sealed class CreateApplicationPaymentRequestDto
{
    [Required] public string PaymentOptionId { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "99999999")] public decimal Amount { get; set; }
}
