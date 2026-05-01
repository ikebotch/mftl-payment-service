using System.ComponentModel.DataAnnotations;

namespace MftlPaymentService.Dtos.v1.Request.MobileMoney;

public class CompleteCollectionRequestDto
{
    [Required] public decimal Amount { get; set; }
    [Required] public string PhoneNumber { get; set; }
    [Required] public string Currency { get; set; }
    [Required] public string Network { get; set; }
    [Required] public string Reference { get; set; }
    [Required] public string UserReference { get; set; }
    [Required] public string Otp { get; set; }
}