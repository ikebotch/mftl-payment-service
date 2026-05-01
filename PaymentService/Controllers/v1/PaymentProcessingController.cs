using Microsoft.AspNetCore.Mvc;
using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Domain;
using MftlPaymentService.Services;

namespace MftlPaymentService.Controllers.v1;

[ApiController]
[Route("api/v1")]
public sealed class PaymentProcessingController(IPaymentOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("payments")]
    public async Task<ActionResult<CreatePaymentResponseDto>> CreatePayment([FromBody] CreatePaymentRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await orchestrator.CreatePaymentAsync(request, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    [HttpGet("payments/{id:guid}")]
    public async Task<ActionResult<PaymentDto>> GetPayment(Guid id, CancellationToken ct)
    {
        var payment = await orchestrator.GetPaymentAsync(id, ct);
        return payment is null ? NotFound(new { message = $"Payment {id} was not found." }) : Ok(payment);
    }

    [HttpGet("payments/reference/{externalReference}")]
    public async Task<ActionResult<PaymentDto>> GetByReference(string externalReference, CancellationToken ct)
    {
        var payment = await orchestrator.GetPaymentByReferenceAsync(externalReference, ct);
        return payment is null ? NotFound(new { message = $"Payment with externalReference {externalReference} was not found." }) : Ok(payment);
    }

    [HttpPost("payments/{id:guid}/verify")]
    public async Task<ActionResult<PaymentDto>> Verify(Guid id, CancellationToken ct)
    {
        try
        {
            return Ok(await orchestrator.VerifyPaymentAsync(id, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    [HttpPost("payments/{id:guid}/refund")]
    public async Task<ActionResult<PaymentDto>> Refund(Guid id, [FromBody] RefundPaymentRequestDto request, CancellationToken ct)
    {
        try
        {
            return Ok(await orchestrator.RefundPaymentAsync(id, request, ct));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new { message = ex.Message });
        }
    }

    [HttpPost("webhooks/stripe")]
    public Task<ActionResult> Stripe(CancellationToken ct) => ProcessWebhook(PaymentProviderType.Stripe, ct);

    [HttpPost("webhooks/paystack")]
    public Task<ActionResult> Paystack(CancellationToken ct) => ProcessWebhook(PaymentProviderType.Paystack, ct);

    [HttpPost("webhooks/moolre")]
    public Task<ActionResult> Moolre(CancellationToken ct) => ProcessWebhook(PaymentProviderType.Moolre, ct);

    private async Task<ActionResult> ProcessWebhook(PaymentProviderType provider, CancellationToken ct)
    {
        var outcome = await orchestrator.ProcessWebhookAsync(provider, Request, ct);
        if (!outcome.Accepted)
            return Unauthorized(new { message = outcome.Message });
        if (outcome.Duplicate)
            return Ok(new { status = "duplicate", message = outcome.Message });
        return Ok(new { status = "processed", payment = outcome.Payment, message = outcome.Message });
    }
}
