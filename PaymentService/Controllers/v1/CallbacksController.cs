using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MftlPaymentService.Dtos.v1.Request.Moolre;
using MftlPaymentService.Dtos.v1.Request.Payments;
using MftlPaymentService.Interfaces.v1;
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
    private readonly IActivityLogService _activityLogService;

    public CallbacksController(
        ILogger<CallbacksController> logger,
        IHttpClientFactory httpClientFactory,
        IPaymentJourneyService paymentJourneyService,
        IActivityLogService activityLogService,
        IOptions<PaymentWebhookSettings> webhookOptions)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _paymentJourneyService = paymentJourneyService;
        _activityLogService = activityLogService;
        _webhookSettings = webhookOptions.Value;
    }

    [HttpPost("moolre")]
    public async Task<IActionResult> Moolre([FromBody] MoolreTransactionWebhookRequestDto body)
    {
        await SafeLog("callback.moolre.transaction", "received", "pending", request: body, provider: "moolre", reference: body.Data?.ExternalRef ?? body.Data?.ThirdPartyRef ?? body.Data?.TransactionId);
        if (body.Data is null)
            return BadRequest(new { message = "data is required" });

        var now = DateTimeOffset.UtcNow;
        var localState = body.Data.TxStatus switch
        {
            1 => "completed",
            2 => "failed",
            _ => "pending"
        };

        var outboundStatus = body.Data.TxStatus switch
        {
            1 => "success",
            2 => "failed",
            _ => "pending"
        };

        decimal amount = 0m;
        if (!string.IsNullOrWhiteSpace(body.Data.Amount))
            decimal.TryParse(body.Data.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

        var paymentId = (body.Data.ExternalRef ?? body.Data.ThirdPartyRef ?? body.Data.TransactionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(paymentId))
            paymentId = $"MOOLRE-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";

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
            await SafeLog("callback.moolre.transaction", "persisted_local", "success", request: body, response: webhookPayload, provider: "moolre", reference: paymentId, paymentReference: paymentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist payment callback paymentId={PaymentId}", paymentId);
            await SafeLog("callback.moolre.transaction", "persisted_local", "failed", request: body, response: webhookPayload, provider: "moolre", reference: paymentId, paymentReference: paymentId, errorMessage: ex.Message);
        }

        var webhookUrl = _webhookSettings.RegistrationPaymentStatusUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                using var response = await client.PostAsJsonAsync(webhookUrl, webhookPayload);

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning(
                        "Payment status webhook forward failed. statusCode={StatusCode} url={Url} body={Body}",
                        (int)response.StatusCode,
                        webhookUrl,
                        responseBody);
                    await SafeLog("callback.moolre.transaction", "forward_registration", "failed",
                        request: webhookPayload,
                        response: new { responseBody, statusCode = (int)response.StatusCode, webhookUrl },
                        provider: "moolre",
                        reference: paymentId,
                        paymentReference: paymentId,
                        errorMessage: "Registration webhook returned non-success status code");
                }
                else
                {
                    await SafeLog("callback.moolre.transaction", "forward_registration", "success",
                        request: webhookPayload,
                        response: new { statusCode = (int)response.StatusCode, webhookUrl },
                        provider: "moolre",
                        reference: paymentId,
                        paymentReference: paymentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Payment status webhook forward threw exception. url={Url} paymentId={PaymentId}",
                    webhookUrl,
                    paymentId);
                await SafeLog("callback.moolre.transaction", "forward_registration", "failed",
                    request: webhookPayload,
                    response: new { webhookUrl },
                    provider: "moolre",
                    reference: paymentId,
                    paymentReference: paymentId,
                    errorMessage: ex.Message);
            }
        }

        _logger.LogInformation(
            "Processed Moolre callback externalRef={ExternalRef} providerRef={ProviderRef} fallbackProviderRef={FallbackProviderRef} state={State} amount={Amount} code={ProviderCode} message={ProviderMessage} processedAt={ProcessedAtUtc}",
            body.Data.ExternalRef?.Trim(),
            body.Data.ThirdPartyRef?.Trim(),
            body.Data.TransactionId?.Trim(),
            localState,
            amount,
            body.Code,
            body.Message,
            now);

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
