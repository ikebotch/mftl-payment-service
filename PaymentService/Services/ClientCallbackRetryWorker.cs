using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MftlPaymentService.Data;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Services;

public class ClientCallbackRetryWorker(
    IServiceProvider serviceProvider,
    ILogger<ClientCallbackRetryWorker> logger) : BackgroundService
{
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _baseDelay = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ClientCallbackRetryWorker is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDeliveriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while processing client callbacks.");
            }

            await Task.Delay(_pollingInterval, stoppingToken);
        }

        logger.LogInformation("ClientCallbackRetryWorker is stopping.");
    }

    public async Task ProcessPendingDeliveriesAsync(CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IClientCallbackDispatcher>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ClientCallbackOptions>>().Value;

        var now = DateTimeOffset.UtcNow;

        // Find pending or failed deliveries that haven't reached max attempts.
        // We use client-side evaluation for the exponential backoff filter since Math.Pow
        // might not translate well to SQL in older EF versions, but we can do a broad filter in SQL.
        var eligibleDeliveries = await dbContext.ClientCallbackDeliveries
            .Include(d => d.PaymentRecord)
            .Where(d => (d.Status == ClientCallbackStatus.Pending || d.Status == ClientCallbackStatus.Failed) 
                     && d.AttemptCount < MaxAttempts)
            .OrderBy(d => d.CreatedAt)
            .Take(100)
            .ToListAsync(stoppingToken);

        foreach (var delivery in eligibleDeliveries)
        {
            // Calculate next attempt time using exponential backoff: BaseDelay * 2^AttemptCount
            // Attempt 0: now
            // Attempt 1: +30s
            // Attempt 2: +60s
            // Attempt 3: +120s
            var delaySeconds = _baseDelay.TotalSeconds * Math.Pow(2, delivery.AttemptCount);
            var nextAttemptAt = (delivery.LastAttemptAt ?? delivery.CreatedAt).AddSeconds(delaySeconds);

            if (now < nextAttemptAt && delivery.AttemptCount > 0)
            {
                continue; // Not ready for retry yet
            }

            var clientApp = delivery.PaymentRecord.ClientApp;
            var sharedSecret = options.Apps.TryGetValue(clientApp, out var appOptions) 
                ? appOptions.SharedSecret 
                : string.Empty;

            if (string.IsNullOrWhiteSpace(sharedSecret))
            {
                delivery.Status = ClientCallbackStatus.Failed;
                delivery.LastError = $"No callback shared secret configured for client app {clientApp}.";
                delivery.AttemptCount++;
                delivery.LastAttemptAt = now;
                logger.LogError("No callback shared secret configured for client app {ClientApp}. Skipping delivery {DeliveryId}.", clientApp, delivery.Id);
                await dbContext.SaveChangesAsync(stoppingToken);
                continue;
            }

            delivery.AttemptCount++;
            delivery.LastAttemptAt = now;

            try
            {
                await dispatcher.DispatchAsync(delivery, sharedSecret, stoppingToken);
                delivery.Status = ClientCallbackStatus.Sent;
                delivery.LastError = null;
                logger.LogInformation("Successfully delivered callback {DeliveryId}.", delivery.Id);
            }
            catch (Exception ex)
            {
                delivery.Status = ClientCallbackStatus.Failed;
                delivery.LastError = ex.Message;
                logger.LogWarning(ex, "Failed to deliver callback {DeliveryId}. Attempt {AttemptCount}/{MaxAttempts}.", delivery.Id, delivery.AttemptCount, MaxAttempts);

                if (delivery.AttemptCount >= MaxAttempts)
                {
                    delivery.Status = ClientCallbackStatus.DeadLetter;
                    logger.LogError("Callback {DeliveryId} reached max attempts and is now dead-lettered.", delivery.Id);
                }
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }
    }
}
