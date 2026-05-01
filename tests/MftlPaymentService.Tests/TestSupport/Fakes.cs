using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Data;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Services;
using MftlPaymentService.Settings;
using System.Text;
using System.Text.Json;

namespace MftlPaymentService.Tests.TestSupport;

internal sealed class FakeProvider(PaymentProviderType providerType) : IPaymentProvider
{
    public PaymentProviderType Provider => providerType;
    public CreatePaymentResult CreateResult { get; set; } = new() { Succeeded = true, ProviderReference = "provider-ref", CheckoutUrl = "https://checkout.test" };
    public VerifyPaymentResult VerifyResult { get; set; } = new() { Succeeded = true, Status = PaymentStatus.Succeeded, ProviderReference = "provider-ref" };
    public WebhookParseResult WebhookResult { get; set; } = new()
    {
        SignatureValid = true,
        EventId = Guid.NewGuid().ToString("N"),
        ProviderReference = "provider-ref",
        Status = PaymentStatus.Succeeded,
        PayloadHash = "hash",
        Payload = JsonDocument.Parse("{}").RootElement.Clone()
    };
    public RefundResult RefundResult { get; set; } = new() { Succeeded = false, Status = PaymentStatus.Failed, FailureReason = "Not implemented" };

    public Task<CreatePaymentResult> CreatePaymentAsync(CreateProviderPaymentRequest request, CancellationToken ct) => Task.FromResult(CreateResult);
    public Task<VerifyPaymentResult> VerifyPaymentAsync(string providerReference, CancellationToken ct) => Task.FromResult(VerifyResult);
    public Task<WebhookParseResult> ParseWebhookAsync(HttpRequest request, CancellationToken ct) => Task.FromResult(WebhookResult);
    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken ct) => Task.FromResult(RefundResult);
}

internal sealed class FakeProviderResolver(params IPaymentProvider[] providers) : IPaymentProviderResolver
{
    private readonly Dictionary<PaymentProviderType, IPaymentProvider> _providers = providers.ToDictionary(x => x.Provider);
    public IPaymentProvider Resolve(PaymentProviderType provider) => _providers[provider];
}

internal sealed class RecordingCallbackDispatcher : IClientCallbackDispatcher
{
    public int CallCount { get; private set; }
    public ClientCallbackDelivery? LastDelivery { get; private set; }
    public string? LastSharedSecret { get; private set; }
    public bool ThrowOnDispatch { get; set; }

    public Task DispatchAsync(ClientCallbackDelivery delivery, string sharedSecret, CancellationToken ct)
    {
        CallCount++;
        LastDelivery = delivery;
        LastSharedSecret = sharedSecret;
        if (ThrowOnDispatch)
            throw new InvalidOperationException("callback failed");
        return Task.CompletedTask;
    }
}

internal static class TestFactory
{
    public static PaymentsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new PaymentsDbContext(options);
    }

    public static PaymentOrchestrator CreateOrchestrator(
        PaymentsDbContext dbContext,
        IClientCallbackDispatcher callbackDispatcher,
        params IPaymentProvider[] providers)
    {
        var options = Options.Create(new ClientCallbackOptions
        {
            Apps = new Dictionary<string, ClientCallbackRegistration>(StringComparer.OrdinalIgnoreCase)
            {
                ["mftl-collections"] = new()
                {
                    SharedSecret = "super-secret",
                    DefaultCallbackUrl = "https://client.test/payments/callback"
                }
            }
        });

        return new PaymentOrchestrator(
            dbContext,
            new FakeProviderResolver(providers),
            callbackDispatcher,
            options,
            NullLogger<PaymentOrchestrator>.Instance);
    }

    public static DefaultHttpContext CreateJsonRequest(string json, string? signatureHeaderName = null, string? signature = null)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/json";
        if (!string.IsNullOrWhiteSpace(signatureHeaderName) && signature is not null)
            context.Request.Headers[signatureHeaderName] = signature;
        return context;
    }
}
