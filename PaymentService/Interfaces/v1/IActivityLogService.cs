namespace MftlPaymentService.Interfaces.v1;

public interface IActivityLogService
{
    Task LogAsync(
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
        CancellationToken cancellationToken = default);
}
