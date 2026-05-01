using Microsoft.AspNetCore.Mvc;
using MftlPaymentService.Dtos.v1.Request.Payments;
using MftlPaymentService.Dtos.v1.Response.Payments;
using MftlPaymentService.Interfaces.v1;

namespace MftlPaymentService.Controllers.v1;

[ApiController]
[Route("api/v1")]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentJourneyService _payments;

    public PaymentsController(IPaymentJourneyService payments)
    {
        _payments = payments;
    }

    [HttpGet("payments/options")]
    public ActionResult<PaymentOptionsResponseDto> GetPaymentOptions()
    {
        return Ok(_payments.GetPaymentOptions());
    }

    [HttpPost("applications/{applicationId:guid}/payments")]
    public async Task<ActionResult<CreateApplicationPaymentResponseDto>> CreateApplicationPayment(
        Guid applicationId,
        [FromBody] CreateApplicationPaymentRequestDto body)
    {
        try
        {
            return Ok(await _payments.CreateApplicationPayment(applicationId, body));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("applications/{applicationId:guid}/payment/summary")]
    public async Task<ActionResult<ApplicationPaymentSummaryResponseDto>> GetApplicationPaymentSummary(Guid applicationId)
    {
        return Ok(await _payments.GetApplicationPaymentSummary(applicationId));
    }

    [HttpPost("payments/webhooks/status")]
    public async Task<ActionResult<PaymentStatusWebhookResponseDto>> PaymentStatusWebhook(
        [FromBody] PaymentStatusWebhookRequestDto body)
    {
        try
        {
            return Ok(await _payments.ProcessPaymentStatusWebhook(body));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
