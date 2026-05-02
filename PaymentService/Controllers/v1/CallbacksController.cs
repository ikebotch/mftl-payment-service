using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Dtos.v1.Request.Moolre;
using MftlPaymentService.Dtos.v1.Request.Payments;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Services;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Controllers.v1;

[ApiController]
[Route("callback/transactions")]
public sealed class CallbacksController : ControllerBase
{
    private readonly ILogger<CallbacksController> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PaymentWebhookSettings _webhookSettings;
    private readonly IPaymentJourneyService _paymentJourneyService;
    private readonly IPaymentOrchestrator _orchestrator;
    private readonly IActivityLogService _activityLogService;

    public CallbacksController(
        ILogger<CallbacksController> logger,
        IHttpClientFactory httpClientFactory,
        IPaymentJourneyService paymentJourneyService,
        IPaymentOrchestrator orchestrator,
        IActivityLogService activityLogService,
        IOptions<PaymentWebhookSettings> webhookOptions,
        MftlPaymentService.Providers.v1.IMoolreProvider moolreProvider)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _paymentJourneyService = paymentJourneyService;
        _orchestrator = orchestrator;
        _activityLogService = activityLogService;
        _webhookSettings = webhookOptions.Value;
        _moolreProvider = moolreProvider;
    }

    private readonly MftlPaymentService.Providers.v1.IMoolreProvider _moolreProvider;

    [HttpGet("moolre/diag-test")]
    public async Task<IActionResult> DiagTestMoolre(string? phone)
    {
        _logger.LogInformation("Manually triggering Moolre diagnostic test.");
        var result = await _moolreProvider.InitiateCollection(new MftlPaymentService.Dtos.v1.Request.MobileMoney.InitiateCollectionRequestDto
        {
            Amount = 1.00m,
            Currency = "GHS",
            PhoneNumber = phone ?? "0244199324",
            Reference = $"DIAG-{Guid.NewGuid():N}",
            Network = "mtn"
        });

        return Ok(result);
    }

    [HttpPost("moolre")]
    public async Task<IActionResult> Moolre(CancellationToken ct)
    {
        _logger.LogInformation("Received Moolre callback on legacy route.");
        
        // Use the unified orchestrator first (handles PaymentRecord entities used by Collections)
        var outcome = await _orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, Request, ct);
        
        if (outcome.Accepted)
        {
            if (outcome.Payment != null)
            {
                return Ok(new { status = "received", paymentId = outcome.Payment.Id });
            }
            
            // If accepted but no payment found, it might be a legacy application payment
            // We'll fall back to the old logic if needed, but the orchestrator now tracks unmatched events too.
            if (outcome.Message == "No matching payment found for webhook.")
            {
                _logger.LogWarning("Moolre callback matched no PaymentRecord. Attempting legacy ApplicationPayment resolution.");
            }
            else
            {
                return Ok(new { status = "received", message = outcome.Message });
            }
        }

        // Fallback to legacy ApplicationPayment logic
        // We need to re-read the body because ProcessWebhookAsync consumed it (but WebhookHelpers enables buffering)
        Request.Body.Position = 0;
        var body = await Request.ReadFromJsonAsync<MoolreTransactionWebhookRequestDto>(cancellationToken: ct);
        if (body?.Data == null)
            return Ok(new { status = "received" }); // Safe acknowledgement even if malformed

        await SafeLog("callback.moolre.transaction", "received", "pending", request: body, provider: "moolre", reference: body.Data?.ExternalRef ?? body.Data?.ThirdPartyRef ?? body.Data?.TransactionId);

        var outboundStatus = body.Data.TxStatus switch
        {
            1 => "success",
            2 => "failed",
            _ => "pending"
        };

        var paymentId = (body.Data.ExternalRef ?? body.Data.ThirdPartyRef ?? body.Data.TransactionId ?? string.Empty).Trim();
        var id = ResolveId(body, paymentId);

        var webhookPayload = new PaymentStatusWebhookRequestDto
        {
            Id = id,
            PaymentId = paymentId,
            Status = outboundStatus
        };

        try
        {
            await _paymentJourneyService.ProcessPaymentStatusWebhook(webhookPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist legacy payment callback paymentId={PaymentId}", paymentId);
        }

        return Ok(new { status = "received" });
    }

    private async Task SafeLog(
        string activity,
        string stage,
        string status,
        object? request = null,
        object? response = null,
        string? provider = null,
        string? reference = null,
        Guid? applicationId = null,
        string? paymentReference = null,
        string? errorMessage = null)
    {
        try
        {
            await _activityLogService.LogAsync(
                activity: activity,
                stage: stage,
                status: status,
                request: request,
                response: response,
                provider: provider,
                reference: reference,
                applicationId: applicationId,
                paymentReference: paymentReference,
                errorMessage: errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist callback activity log. activity={Activity}, stage={Stage}", activity, stage);
        }
    }

    private static string ResolveId(MoolreTransactionWebhookRequestDto body, string paymentId)
    {
        var candidates = new[]
        {
            body.Data?.Secret,
            body.Data?.ExternalRef,
            body.Data?.ThirdPartyRef,
            paymentId
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return Guid.NewGuid().ToString("D");
    }
}
