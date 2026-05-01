using Microsoft.EntityFrameworkCore;
using MftlPaymentService.Data;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Dtos.v1.Request.Payments;
using MftlPaymentService.Dtos.v1.Response.Payments;
using MftlPaymentService.Interfaces.v1;

namespace MftlPaymentService.Services.v1;

public sealed class PaymentJourneyService : IPaymentJourneyService
{
    private static readonly IReadOnlyList<PaymentOptionDto> PaymentOptions =
    [
        new("card", "Card", "https://cdn.jsdelivr.net/gh/simple-icons/simple-icons/icons/visa.svg", "Pay with debit or credit card."),
        new("mobile-money", "Mobile Money", null, "Pay with mobile money wallet."),
        new("bank-transfer", "Bank Transfer", null, "Pay from your bank account.")
    ];

    private readonly PaymentsDbContext _db;
    private readonly IActivityLogService _activityLog;

    public PaymentJourneyService(PaymentsDbContext db, IActivityLogService activityLog)
    {
        _db = db;
        _activityLog = activityLog;
    }

    public PaymentOptionsResponseDto GetPaymentOptions() => new(PaymentOptions);

    public async Task<CreateApplicationPaymentResponseDto> CreateApplicationPayment(Guid applicationId, CreateApplicationPaymentRequestDto body)
    {
        await SafeLog("payments.application.create", "received", "pending", request: body, applicationId: applicationId);
        if (string.IsNullOrWhiteSpace(body.PaymentOptionId))
            throw new ArgumentException("paymentOptionId is required.", nameof(body));

        var optionId = body.PaymentOptionId.Trim();
        var optionExists = PaymentOptions.Any(x => string.Equals(x.Id, optionId, StringComparison.OrdinalIgnoreCase));
        if (!optionExists)
            throw new ArgumentException("Invalid paymentOptionId.", nameof(body));

        if (body.Amount <= 0)
            throw new ArgumentException("amount must be greater than zero.", nameof(body));

        var reference = await GenerateUniqueReference(applicationId);

        var payment = new ApplicationPayment
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            PaymentReference = reference,
            PaymentOptionId = optionId,
            Amount = body.Amount,
            Currency = "GHS",
            Status = "pending",
            Verified = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _db.ApplicationPayments.Add(payment);
        await _db.SaveChangesAsync();
        await SafeLog("payments.application.create", "persisted", "success", request: body, response: payment, applicationId: applicationId, paymentReference: reference, reference: reference);

        return new CreateApplicationPaymentResponseDto(
            PaymentReference: reference,
            Status: payment.Status,
            Verified: payment.Verified,
            ReceiptUrl: $"/api/v1/applications/{applicationId}/payments/{reference}/receipt");
    }

    public async Task<ApplicationPaymentSummaryResponseDto> GetApplicationPaymentSummary(Guid applicationId)
    {
        await SafeLog("payments.application.summary", "received", "pending", applicationId: applicationId);
        var payments = await _db.ApplicationPayments
            .Where(x => x.ApplicationId == applicationId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        var latest = payments.FirstOrDefault();
        var alreadyPaid = payments.Any(x => x.Verified);

        var summary = new ApplicationPaymentSummaryResponseDto(
            ServiceName: "registration-service",
            ServiceCost: 120m,
            Currency: "GHS",
            CanPay: !alreadyPaid,
            AlreadyPaid: alreadyPaid,
            PaymentReference: latest?.PaymentReference);

        await SafeLog("payments.application.summary", "completed", "success", response: summary, applicationId: applicationId, paymentReference: summary.PaymentReference, reference: summary.PaymentReference);

        return summary;
    }

    public async Task<PaymentStatusWebhookResponseDto> ProcessPaymentStatusWebhook(PaymentStatusWebhookRequestDto body)
    {
        await SafeLog("payments.webhook.status", "received", "pending", request: body, reference: body.PaymentId, paymentReference: body.PaymentId);
        if (string.IsNullOrWhiteSpace(body.Id))
            throw new ArgumentException("id is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(body.PaymentId))
            throw new ArgumentException("paymentId is required.", nameof(body));
        if (string.IsNullOrWhiteSpace(body.Status))
            throw new ArgumentException("status is required.", nameof(body));

        var normalizedStatus = NormalizeStatus(body.Status);
        var isVerified = normalizedStatus == "completed";
        var id = body.Id.Trim();
        var paymentId = body.PaymentId.Trim();

        var payment = await _db.ApplicationPayments
            .FirstOrDefaultAsync(x => x.PaymentReference == paymentId);

        if (payment is null && Guid.TryParse(id, out var parsedApplicationId))
        {
            payment = await _db.ApplicationPayments
                .Where(x => x.ApplicationId == parsedApplicationId)
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (payment is null)
        {
            payment = new ApplicationPayment
            {
                Id = Guid.NewGuid(),
                ApplicationId = Guid.TryParse(id, out var parsedId) ? parsedId : Guid.NewGuid(),
                PaymentReference = paymentId,
                PaymentOptionId = "mobile-money",
                Amount = 0m,
                Currency = "GHS",
                Status = normalizedStatus,
                Verified = isVerified,
                ProviderTransactionId = id,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _db.ApplicationPayments.Add(payment);
        }
        else
        {
            payment.Status = normalizedStatus;
            payment.Verified = isVerified;
            payment.ProviderTransactionId = id;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _db.ApplicationPaymentStatusEvents.Add(new ApplicationPaymentStatusEvent
        {
            ApplicationPaymentId = payment.Id,
            ExternalId = id,
            RawStatus = body.Status.Trim(),
            NormalizedStatus = normalizedStatus,
            ReceivedAtUtc = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync();
        await SafeLog("payments.webhook.status", "persisted", "success", request: body, response: payment, applicationId: payment.ApplicationId, paymentReference: payment.PaymentReference, reference: body.PaymentId);

        var result = new PaymentStatusWebhookResponseDto(
            Status: "received",
            Message: "Payment callback processed.",
            Id: id,
            PaymentId: payment.PaymentReference,
            PaymentStatus: normalizedStatus,
            Verified: isVerified,
            ProcessedAtUtc: DateTimeOffset.UtcNow);

        await SafeLog("payments.webhook.status", "responded", "success", response: result, applicationId: payment.ApplicationId, paymentReference: payment.PaymentReference, reference: body.PaymentId);
        return result;
    }

    private async Task<string> GenerateUniqueReference(Guid applicationId)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var suffix = attempt == 0 ? string.Empty : $"-{attempt}";
            var candidate = $"PAY-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{applicationId.ToString()[..8].ToUpperInvariant()}{suffix}";
            var exists = await _db.ApplicationPayments.AnyAsync(x => x.PaymentReference == candidate);
            if (!exists)
                return candidate;

            await Task.Delay(5);
        }

        return $"PAY-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{applicationId.ToString()[..8].ToUpperInvariant()}";
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "success" or "completed" or "paid" or "verified" => "completed",
            "failed" or "declined" or "cancelled" or "canceled" => "failed",
            _ => "pending"
        };
    }

    private async Task SafeLog(
        string activity,
        string stage,
        string status,
        object? request = null,
        object? response = null,
        string? reference = null,
        Guid? applicationId = null,
        string? paymentReference = null,
        string? errorMessage = null)
    {
        try
        {
            await _activityLog.LogAsync(
                activity: activity,
                stage: stage,
                status: status,
                request: request,
                response: response,
                reference: reference,
                applicationId: applicationId,
                paymentReference: paymentReference,
                errorMessage: errorMessage);
        }
        catch
        {
            // Do not break primary payment flow if telemetry persistence fails.
        }
    }
}
