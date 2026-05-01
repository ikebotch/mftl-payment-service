using Microsoft.AspNetCore.Mvc;
using MftlPaymentService.Domain;
using MftlPaymentService.Services;

namespace MftlPaymentService.Controllers.v1;

[ApiController]
public sealed class GoCardlessCallbackController(IPaymentOrchestrator orchestrator) : ControllerBase
{
    [HttpPost("/callback/transactions/gocardless")]
    public async Task<ActionResult> Callback(CancellationToken ct)
    {
        var outcome = await orchestrator.ProcessWebhookAsync(PaymentProviderType.GoCardless, Request, ct);
        if (!outcome.Accepted)
            return StatusCode(498, new { message = outcome.Message });

        if (outcome.Duplicate)
            return Ok(new { status = "duplicate", message = outcome.Message });

        return Ok(new { status = "processed", payment = outcome.Payment, message = outcome.Message });
    }
}
