using System.Text.Json;
using MftlPaymentService.Data;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Interfaces.v1;

namespace MftlPaymentService.Services.v1;

public sealed class ActivityLogService : IActivityLogService
{
    private readonly PaymentsDbContext _db;

    public ActivityLogService(PaymentsDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        string activity,
        string stage,
        string status,
        object? request = null,
        object? response = null,
        string? provider = null,
        string? reference = null,
        Guid? applicationId = null,
        string? paymentReference = null,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var log = new ActivityLog
        {
            Activity = activity,
            Stage = stage,
            Status = status,
            Provider = provider,
            Reference = reference,
            ApplicationId = applicationId,
            PaymentReference = paymentReference,
            RequestPayload = SerializeOrNull(request),
            ResponsePayload = SerializeOrNull(response),
            ErrorMessage = errorMessage,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _db.Set<ActivityLog>().Add(log);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? SerializeOrNull(object? value)
    {
        if (value is null)
            return null;

        return JsonSerializer.Serialize(value);
    }
}
