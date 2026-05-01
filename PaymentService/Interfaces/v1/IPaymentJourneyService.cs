using MftlPaymentService.Dtos.v1.Request.Payments;
using MftlPaymentService.Dtos.v1.Response.Payments;

namespace MftlPaymentService.Interfaces.v1;

public interface IPaymentJourneyService
{
    PaymentOptionsResponseDto GetPaymentOptions();
    Task<CreateApplicationPaymentResponseDto> CreateApplicationPayment(Guid applicationId, CreateApplicationPaymentRequestDto body);
    Task<ApplicationPaymentSummaryResponseDto> GetApplicationPaymentSummary(Guid applicationId);
    Task<PaymentStatusWebhookResponseDto> ProcessPaymentStatusWebhook(PaymentStatusWebhookRequestDto body);
}
