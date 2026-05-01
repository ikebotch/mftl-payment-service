using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Data;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Services;

public sealed class PaymentOrchestrator(
    PaymentsDbContext dbContext,
    IPaymentProviderResolver providerResolver,
    IClientCallbackDispatcher callbackDispatcher,
    IOptions<ClientCallbackOptions> callbackOptions,
    ILogger<PaymentOrchestrator> logger) : IPaymentOrchestrator
{
    private readonly ClientCallbackOptions _callbackOptions = callbackOptions.Value;

    public async Task<CreatePaymentResponseDto> CreatePaymentAsync(CreatePaymentRequestDto request, CancellationToken ct)
    {
        logger.LogInformation("[DEBUG] PaymentOrchestrator: Received CreatePayment request for ClientApp={ClientApp}, ExternalReference={Reference}, ContributionId={ContributionId}", 
            request.ClientApp, request.ExternalReference, request.ContributionId);
        ValidateCreateRequest(request);

        var existing = await dbContext.PaymentRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ClientApp == request.ClientApp && x.ExternalReference == request.ExternalReference, ct);

        if (existing is not null)
        {
            logger.LogInformation("[DEBUG] PaymentOrchestrator: Found existing payment record {PaymentId} for ExternalReference={Reference}. Returning existing.", existing.Id, request.ExternalReference);
            return MapCreateResponse(existing);
        }

        var payment = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            ClientApp = request.ClientApp.Trim(),
            TenantId = request.TenantId,
            BranchId = request.BranchId,
            ContributionId = request.ContributionId,
            ExternalReference = request.ExternalReference.Trim(),
            Provider = request.Provider,
            Amount = request.Amount,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? ResolveDefaultCurrency(request.Provider) : request.Currency.Trim().ToUpperInvariant(),
            CustomerEmail = request.CustomerEmail?.Trim(),
            CustomerPhone = request.CustomerPhone?.Trim(),
            Description = request.Description?.Trim(),
            CallbackUrl = request.CallbackUrl?.Trim(),
            MetadataJson = request.Metadata?.ValueKind is null ? "{}" : request.Metadata.Value.GetRawText(),
            Status = PaymentStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var provider = providerResolver.Resolve(request.Provider);
        var createResult = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = payment.ClientApp,
            ExternalReference = payment.ExternalReference,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CustomerEmail = payment.CustomerEmail,
            CustomerPhone = payment.CustomerPhone,
            Description = payment.Description,
            Metadata = EnrichProviderMetadata(payment)
        }, ct);

        if (!createResult.Succeeded)
            throw new InvalidOperationException(createResult.FailureReason ?? "Provider payment creation failed.");

        payment.ProviderReference = createResult.ProviderReference;
        payment.ProviderTransactionId = createResult.ProviderTransactionId;
        payment.CheckoutUrl = createResult.CheckoutUrl;

        dbContext.PaymentRecords.Add(payment);
        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation("[DEBUG] PaymentOrchestrator: Created NEW payment {PaymentId} for {ClientApp} via {Provider}. ContributionId={ContributionId}", payment.Id, payment.ClientApp, payment.Provider, payment.ContributionId);
        return MapCreateResponse(payment);
    }

    public async Task<PaymentDto?> GetPaymentAsync(Guid id, CancellationToken ct)
    {
        var payment = await dbContext.PaymentRecords.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return payment is null ? null : MapPayment(payment);
    }

    public async Task<PaymentDto?> GetPaymentByReferenceAsync(string externalReference, CancellationToken ct)
    {
        var payment = await dbContext.PaymentRecords.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExternalReference == externalReference, ct);
        return payment is null ? null : MapPayment(payment);
    }

    public async Task<PaymentDto> VerifyPaymentAsync(Guid id, CancellationToken ct)
    {
        var payment = await dbContext.PaymentRecords.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Payment {id} was not found.");

        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
            throw new InvalidOperationException("Payment has no provider reference to verify.");

        var provider = providerResolver.Resolve(payment.Provider);
        var verification = await provider.VerifyPaymentAsync(payment.ProviderReference, ct);
        ApplyVerification(payment, verification);
        await dbContext.SaveChangesAsync(ct);
        await DispatchTerminalCallbackAsync(payment, ct);
        await dbContext.SaveChangesAsync(ct);

        return MapPayment(payment);
    }

    public async Task<PaymentDto> RefundPaymentAsync(Guid id, RefundPaymentRequestDto request, CancellationToken ct)
    {
        var payment = await dbContext.PaymentRecords.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException($"Payment {id} was not found.");

        if (string.IsNullOrWhiteSpace(payment.ProviderReference))
            throw new InvalidOperationException("Payment has no provider reference to refund.");

        var provider = providerResolver.Resolve(payment.Provider);
        var refundResult = await provider.RefundAsync(new RefundPaymentRequest
        {
            ProviderReference = payment.ProviderReference,
            Amount = request.Amount,
            Reason = request.Reason
        }, ct);

        if (!refundResult.Succeeded)
            throw new NotSupportedException(refundResult.FailureReason ?? "Refund is not supported.");

        payment.Status = refundResult.Status;
        payment.ProviderTransactionId = refundResult.ProviderTransactionId ?? payment.ProviderTransactionId;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        payment.CompletedAt = payment.Status == PaymentStatus.Refunded ? DateTimeOffset.UtcNow : payment.CompletedAt;
        await dbContext.SaveChangesAsync(ct);

        return MapPayment(payment);
    }

    public async Task<WebhookProcessOutcome> ProcessWebhookAsync(PaymentProviderType providerType, HttpRequest request, CancellationToken ct)
    {
        var provider = providerResolver.Resolve(providerType);
        var webhook = await provider.ParseWebhookAsync(request, ct);
        if (!webhook.SignatureValid)
            return new WebhookProcessOutcome(false, false, webhook.FailureReason ?? "Invalid signature.", null);

        var duplicate = await dbContext.ProcessedWebhookEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Provider == providerType &&
                (
                    (!string.IsNullOrWhiteSpace(webhook.EventId) && x.EventId == webhook.EventId) ||
                    (!string.IsNullOrWhiteSpace(webhook.ProviderReference) && x.ProviderReference == webhook.ProviderReference && x.PayloadHash == webhook.PayloadHash)
                ), ct);

        if (duplicate is not null)
            return new WebhookProcessOutcome(true, true, "Duplicate webhook ignored.", duplicate.PaymentRecordId is Guid paymentId ? await GetPaymentAsync(paymentId, ct) : null);

        var payment = await dbContext.PaymentRecords
            .FirstOrDefaultAsync(x =>
                (webhook.ProviderReference != null && x.ProviderReference == webhook.ProviderReference) ||
                (webhook.ProviderTransactionId != null && x.ProviderTransactionId == webhook.ProviderTransactionId) ||
                (webhook.ProviderReference != null && x.ExternalReference == webhook.ProviderReference), ct);

        var processedEvent = new ProcessedWebhookEvent
        {
            Id = Guid.NewGuid(),
            Provider = providerType,
            EventId = webhook.EventId,
            ProviderReference = webhook.ProviderReference,
            PayloadHash = webhook.PayloadHash,
            Status = WebhookProcessingStatus.Pending,
            ProcessedAt = DateTimeOffset.UtcNow
        };

        if (payment is null)
        {
            processedEvent.Status = WebhookProcessingStatus.Failed;
            processedEvent.Error = "No matching payment found for webhook.";
            dbContext.ProcessedWebhookEvents.Add(processedEvent);
            await dbContext.SaveChangesAsync(ct);
            return new WebhookProcessOutcome(true, false, processedEvent.Error, null);
        }

        processedEvent.PaymentRecordId = payment.Id;
        var previousStatus = payment.Status;
        var statusChanged = false;

        if (webhook.Status is not null)
        {
            var validationError = providerType == PaymentProviderType.Mollie
                ? ValidateMollieWebhookBoundary(payment, webhook)
                : null;
            if (validationError is not null)
            {
                processedEvent.Status = WebhookProcessingStatus.Failed;
                processedEvent.Error = validationError;
                dbContext.ProcessedWebhookEvents.Add(processedEvent);
                await dbContext.SaveChangesAsync(ct);
                logger.LogWarning("Rejected Mollie webhook for payment {PaymentId}: {Error}", payment.Id, validationError);
                return new WebhookProcessOutcome(true, false, validationError, MapPayment(payment));
            }

            if (webhook.Amount.HasValue && Math.Abs(webhook.Amount.Value - payment.Amount) > 0.01m)
            {
                logger.LogWarning("Webhook amount mismatch for payment {PaymentId}. Expected {Expected}, Got {Got}.", payment.Id, payment.Amount, webhook.Amount.Value);
            }

            if (!string.IsNullOrWhiteSpace(webhook.Currency) && !string.Equals(webhook.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Webhook currency mismatch for payment {PaymentId}. Expected {Expected}, Got {Got}.", payment.Id, payment.Currency, webhook.Currency);
            }

            statusChanged = ApplyStatusTransition(payment, webhook.Status.Value, webhook.FailureReason);
            
            if (!string.IsNullOrWhiteSpace(webhook.ProviderTransactionId))
            {
                payment.ProviderTransactionId = webhook.ProviderTransactionId;
            }

            if (IsTerminalCallbackStatus(payment.Status) && payment.CompletedAt == null)
            {
                payment.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        processedEvent.Status = WebhookProcessingStatus.Processed;
        dbContext.ProcessedWebhookEvents.Add(processedEvent);
        await dbContext.SaveChangesAsync(ct);
        if (statusChanged && IsTerminalCallbackStatus(payment.Status) && previousStatus != payment.Status)
        {
            await DispatchTerminalCallbackAsync(payment, ct);
            await dbContext.SaveChangesAsync(ct);
        }

        return new WebhookProcessOutcome(true, false, "Webhook processed.", MapPayment(payment));
    }

    private async Task DispatchTerminalCallbackAsync(PaymentRecord payment, CancellationToken ct)
    {
        var eventType = payment.Status switch
        {
            PaymentStatus.Succeeded => "PaymentSucceeded",
            PaymentStatus.Failed or PaymentStatus.Cancelled => "PaymentFailed",
            _ => null
        };

        if (eventType is null)
            return;

        var callbackConfig = _callbackOptions.Apps.TryGetValue(payment.ClientApp, out var config) ? config : null;
        var callbackUrl = string.IsNullOrWhiteSpace(payment.CallbackUrl) ? callbackConfig?.DefaultCallbackUrl : payment.CallbackUrl;
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            logger.LogWarning("No callback URL configured for client app {ClientApp}.", payment.ClientApp);
            return;
        }

        var delivery = await dbContext.ClientCallbackDeliveries
            .FirstOrDefaultAsync(x => x.PaymentRecordId == payment.Id && x.EventType == eventType, ct);

        if (delivery is null)
        {
            delivery = new ClientCallbackDelivery
            {
                Id = Guid.NewGuid(),
                PaymentRecordId = payment.Id,
                EventType = eventType,
                CallbackUrl = callbackUrl,
                PayloadJson = JsonSerializer.Serialize(BuildCallbackPayload(payment), new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } 
                }),
                Status = ClientCallbackStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.ClientCallbackDeliveries.Add(delivery);
            await dbContext.SaveChangesAsync(ct);
        }
        else if (delivery.Status == ClientCallbackStatus.Sent)
        {
            return;
        }

        if (callbackConfig is null || string.IsNullOrWhiteSpace(callbackConfig.SharedSecret))
        {
            delivery.Status = ClientCallbackStatus.Failed;
            delivery.LastError = $"No callback shared secret configured for client app {payment.ClientApp}.";
            delivery.AttemptCount += 1;
            delivery.LastAttemptAt = DateTimeOffset.UtcNow;
            return;
        }

        try
        {
            await callbackDispatcher.DispatchAsync(delivery, callbackConfig.SharedSecret, ct);
            delivery.Status = ClientCallbackStatus.Sent;
            delivery.LastError = null;
            delivery.AttemptCount += 1;
            delivery.LastAttemptAt = DateTimeOffset.UtcNow;
            logger.LogInformation("Sent client callback for payment {PaymentId} event {EventType}", payment.Id, eventType);
        }
        catch (Exception ex)
        {
            delivery.Status = ClientCallbackStatus.Failed;
            delivery.LastError = ex.Message;
            delivery.AttemptCount += 1;
            delivery.LastAttemptAt = DateTimeOffset.UtcNow;
            logger.LogError(ex, "Failed to deliver callback for payment {PaymentId}", payment.Id);
        }
    }

    private static void ApplyVerification(PaymentRecord payment, VerifyPaymentResult verification)
    {
        payment.ProviderReference = verification.ProviderReference ?? payment.ProviderReference;
        payment.ProviderTransactionId = verification.ProviderTransactionId ?? payment.ProviderTransactionId;
        ApplyStatusTransition(payment, verification.Status, verification.FailureReason);
    }

    private static void ValidateCreateRequest(CreatePaymentRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ClientApp))
            throw new ArgumentException("clientApp is required.");
        if (string.IsNullOrWhiteSpace(request.ExternalReference))
            throw new ArgumentException("externalReference is required.");
        if (request.Amount <= 0)
            throw new ArgumentException("amount must be greater than zero.");
    }

    private string ResolveDefaultCurrency(PaymentProviderType provider) => provider switch
    {
        PaymentProviderType.Stripe => "USD",
        PaymentProviderType.Paystack => "GHS",
        PaymentProviderType.GoCardless => "GBP",
        PaymentProviderType.Mollie => "EUR",
        _ => "GHS"
    };

    private static CreatePaymentResponseDto MapCreateResponse(PaymentRecord payment) =>
        new(payment.Id, payment.Status, payment.Provider, payment.ProviderReference, payment.CheckoutUrl, payment.ExternalReference);

    private static PaymentDto MapPayment(PaymentRecord payment) =>
        new()
        {
            Id = payment.Id,
            ClientApp = payment.ClientApp,
            TenantId = payment.TenantId,
            BranchId = payment.BranchId,
            ContributionId = payment.ContributionId,
            ExternalReference = payment.ExternalReference,
            Provider = payment.Provider,
            ProviderReference = payment.ProviderReference,
            ProviderTransactionId = payment.ProviderTransactionId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            CustomerEmail = payment.CustomerEmail,
            CustomerPhone = payment.CustomerPhone,
            Description = payment.Description,
            CallbackUrl = payment.CallbackUrl,
            Metadata = ParseMetadata(payment.MetadataJson),
            Status = payment.Status,
            FailureReason = payment.FailureReason,
            CheckoutUrl = payment.CheckoutUrl,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt,
            CompletedAt = payment.CompletedAt
        };

    private static ClientCallbackPayload BuildCallbackPayload(PaymentRecord payment) => new()
    {
        CallbackEventId = $"{payment.Id:N}:{(payment.Status == PaymentStatus.Succeeded ? "PaymentSucceeded" : "PaymentFailed")}",
        EventType = payment.Status == PaymentStatus.Succeeded ? "PaymentSucceeded" : "PaymentFailed",
        PaymentServicePaymentId = payment.Id,
        TenantId = payment.TenantId,
        ContributionId = payment.ContributionId,
        Provider = payment.Provider,
        ProviderReference = payment.ProviderReference,
        ProviderTransactionId = payment.ProviderTransactionId,
        ClientApp = payment.ClientApp,
        ExternalReference = payment.ExternalReference,
        Amount = payment.Amount,
        Currency = payment.Currency,
        Status = payment.Status,
        OccurredAt = payment.CompletedAt ?? payment.UpdatedAt,
        Metadata = ParseMetadata(payment.MetadataJson)
    };

    private static JsonElement ParseMetadata(string rawJson)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);
        return document.RootElement.Clone();
    }

    private static JsonElement EnrichProviderMetadata(PaymentRecord payment)
    {
        var metadata = ParseMetadata(payment.MetadataJson);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (metadata.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadata.EnumerateObject())
            {
                values[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.GetRawText();
            }
        }

        values["tenantId"] = payment.TenantId;
        values["branchId"] = payment.BranchId;
        values["contributionId"] = payment.ContributionId;
        values["externalReference"] = payment.ExternalReference;

        return JsonSerializer.SerializeToElement(values, JsonSerializerOptions.Web);
    }

    private static bool ApplyStatusTransition(PaymentRecord payment, PaymentStatus nextStatus, string? failureReason)
    {
        if (payment.Status == PaymentStatus.Succeeded && nextStatus != PaymentStatus.Succeeded)
        {
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            return false;
        }

        if (payment.Status == nextStatus)
        {
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            return false;
        }

        payment.Status = nextStatus;
        payment.FailureReason = nextStatus == PaymentStatus.Failed ? failureReason ?? payment.FailureReason : null;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        if (payment.Status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded)
            payment.CompletedAt = payment.CompletedAt ?? DateTimeOffset.UtcNow;

        return true;
    }

    private static bool IsTerminalCallbackStatus(PaymentStatus status) =>
        status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled;

    private static string? ValidateMollieWebhookBoundary(PaymentRecord payment, WebhookParseResult webhook)
    {
        if (webhook.Amount.HasValue && ToMinorUnits(webhook.Amount.Value) != ToMinorUnits(payment.Amount))
            return "Mollie amount mismatch.";

        if (!string.IsNullOrWhiteSpace(webhook.Currency) && !string.Equals(webhook.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            return "Mollie currency mismatch.";

        var metadata = webhook.Payload.TryGetProperty("metadata", out var metadataElement) ? metadataElement : default;
        if (metadata.ValueKind != JsonValueKind.Object)
            return null;

        var tenantId = ReadMetadataString(metadata, "tenantId");
        if (!string.IsNullOrWhiteSpace(tenantId) && (!payment.TenantId.HasValue || !Guid.TryParse(tenantId, out var parsedTenantId) || parsedTenantId != payment.TenantId.Value))
            return "Mollie tenant mismatch.";

        var contributionId = ReadMetadataString(metadata, "contributionId");
        if (!string.IsNullOrWhiteSpace(contributionId) && (!payment.ContributionId.HasValue || !Guid.TryParse(contributionId, out var parsedContributionId) || parsedContributionId != payment.ContributionId.Value))
            return "Mollie contribution mismatch.";

        var externalReference = ReadMetadataString(metadata, "externalReference");
        if (!string.IsNullOrWhiteSpace(externalReference) && !string.Equals(externalReference, payment.ExternalReference, StringComparison.Ordinal))
            return "Mollie external reference mismatch.";

        return null;
    }

    private static string? ReadMetadataString(JsonElement metadata, string propertyName)
    {
        foreach (var property in metadata.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                continue;

            return property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.GetRawText();
        }

        return null;
    }

    private static long ToMinorUnits(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}
