using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using MftlPaymentService.Settings;
using MftlPaymentService.Infrastructure.Providers;
using System.Text.Json;

namespace MftlPaymentService.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalAuthAttribute : Attribute, IAsyncResourceFilter
{
    private static readonly TimeSpan MaxDrift = TimeSpan.FromMinutes(5);

    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<InternalAuthAttribute>>();
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<ClientCallbackOptions>>().Value;

        var request = context.HttpContext.Request;
        var timestampHeader = request.Headers["X-MFTL-Timestamp"].FirstOrDefault();
        var signatureHeader = request.Headers["X-MFTL-Signature"].FirstOrDefault();
        var clientAppHeader = request.Headers["X-MFTL-Client-App"].FirstOrDefault();

        if (string.IsNullOrEmpty(timestampHeader) || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(clientAppHeader))
        {
            logger.LogWarning("Rejecting internal request: missing security headers. ClientApp={ClientApp}, Timestamp={Timestamp}, Signature={Signature}", 
                clientAppHeader ?? "MISSING", timestampHeader ?? "MISSING", signatureHeader ?? "MISSING");
            context.Result = new ObjectResult(new { message = "Missing security headers." }) { StatusCode = 403 };
            return;
        }

        if (!long.TryParse(timestampHeader, out var timestampSeconds))
        {
            logger.LogWarning("Rejecting internal request: invalid timestamp format.");
            context.Result = new ObjectResult(new { message = "Invalid timestamp format." }) { StatusCode = 403 };
            return;
        }

        var timestampDate = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds);
        if (Math.Abs((DateTimeOffset.UtcNow - timestampDate).TotalMinutes) > MaxDrift.TotalMinutes)
        {
            logger.LogWarning("Rejecting internal request: timestamp drift exceeds allowed limit.");
            context.Result = new ObjectResult(new { message = "Timestamp expired/stale." }) { StatusCode = 403 };
            return;
        }

        if (!options.Apps.TryGetValue(clientAppHeader, out var appRegistration) || string.IsNullOrEmpty(appRegistration.SharedSecret))
        {
            logger.LogWarning("Rejecting internal request: unknown or misconfigured client app {ClientApp}. Available apps: {AvailableApps}", 
                clientAppHeader, string.Join(", ", options.Apps.Keys));
            context.Result = new ObjectResult(new { message = "Unknown client app." }) { StatusCode = 403 };
            return;
        }

        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (!VerifySignature(appRegistration.SharedSecret, timestampHeader, body, signatureHeader))
        {
            var computed = WebhookHelpers.ComputeHmacSha256Hex(appRegistration.SharedSecret, $"{timestampHeader}.{body}");
            logger.LogWarning("Rejecting internal request: invalid signature for client app {ClientApp}. Expected={Expected}, Computed={Computed}", 
                clientAppHeader, signatureHeader, computed);
            context.Result = new ObjectResult(new { message = "Invalid signature." }) { StatusCode = 403 };
            return;
        }

        await next();
    }

    private static bool VerifySignature(string secret, string timestamp, string payload, string expectedSignature)
    {
        var computedSignature = WebhookHelpers.ComputeHmacSha256Hex(secret, $"{timestamp}.{payload}");
        return WebhookHelpers.FixedTimeEquals(computedSignature, expectedSignature);
    }
}
