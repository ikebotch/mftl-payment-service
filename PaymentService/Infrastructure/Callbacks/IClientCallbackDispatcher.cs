using MftlPaymentService.Data.Entities;

namespace MftlPaymentService.Infrastructure.Callbacks;

public interface IClientCallbackDispatcher
{
    Task DispatchAsync(ClientCallbackDelivery delivery, string sharedSecret, CancellationToken ct);
}
