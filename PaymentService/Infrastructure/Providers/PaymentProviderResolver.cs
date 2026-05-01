using MftlPaymentService.Domain;

namespace MftlPaymentService.Infrastructure.Providers;

public sealed class PaymentProviderResolver(IEnumerable<IPaymentProvider> providers) : IPaymentProviderResolver
{
    private readonly IReadOnlyDictionary<PaymentProviderType, IPaymentProvider> _providers =
        providers.ToDictionary(x => x.Provider);

    public IPaymentProvider Resolve(PaymentProviderType provider) =>
        _providers.TryGetValue(provider, out var paymentProvider)
            ? paymentProvider
            : throw new KeyNotFoundException($"No payment provider registered for {provider}.");
}
