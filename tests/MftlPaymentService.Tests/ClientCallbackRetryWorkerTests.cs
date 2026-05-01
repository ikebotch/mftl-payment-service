using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Data;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Services;
using MftlPaymentService.Settings;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class ClientCallbackRetryWorkerTests
{
    [Fact]
    public async Task ProcessPendingDeliveriesAsync_uses_payment_record_client_app_secret()
    {
        await using var db = TestFactory.CreateDbContext();
        var dispatcher = new RecordingCallbackDispatcher();
        var payment = AddPaymentWithDelivery(db, "tenant-billing");
        var worker = CreateWorker(db, dispatcher, new Dictionary<string, ClientCallbackRegistration>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant-billing"] = new() { SharedSecret = "tenant-billing-secret" },
            ["mftl-collections"] = new() { SharedSecret = "wrong-secret" }
        });

        await worker.ProcessPendingDeliveriesAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal("tenant-billing-secret", dispatcher.LastSharedSecret);
        Assert.Equal(ClientCallbackStatus.Sent, db.ClientCallbackDeliveries.Single(d => d.PaymentRecordId == payment.Id).Status);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_missing_client_secret_fails_safely()
    {
        await using var db = TestFactory.CreateDbContext();
        var dispatcher = new RecordingCallbackDispatcher();
        var payment = AddPaymentWithDelivery(db, "unconfigured-app");
        var worker = CreateWorker(db, dispatcher, new Dictionary<string, ClientCallbackRegistration>(StringComparer.OrdinalIgnoreCase)
        {
            ["mftl-collections"] = new() { SharedSecret = "collections-secret" }
        });

        await worker.ProcessPendingDeliveriesAsync(CancellationToken.None);

        var delivery = db.ClientCallbackDeliveries.Single(d => d.PaymentRecordId == payment.Id);
        Assert.Equal(0, dispatcher.CallCount);
        Assert.Equal(ClientCallbackStatus.Failed, delivery.Status);
        Assert.Contains("unconfigured-app", delivery.LastError);
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_mftl_collections_still_works()
    {
        await using var db = TestFactory.CreateDbContext();
        var dispatcher = new RecordingCallbackDispatcher();
        AddPaymentWithDelivery(db, "mftl-collections");
        var worker = CreateWorker(db, dispatcher, new Dictionary<string, ClientCallbackRegistration>(StringComparer.OrdinalIgnoreCase)
        {
            ["mftl-collections"] = new() { SharedSecret = "collections-secret" }
        });

        await worker.ProcessPendingDeliveriesAsync(CancellationToken.None);

        Assert.Equal(1, dispatcher.CallCount);
        Assert.Equal("collections-secret", dispatcher.LastSharedSecret);
        Assert.Equal(ClientCallbackStatus.Sent, db.ClientCallbackDeliveries.Single().Status);
    }

    private static PaymentRecord AddPaymentWithDelivery(PaymentsDbContext db, string clientApp)
    {
        var payment = new PaymentRecord
        {
            Id = Guid.NewGuid(),
            ClientApp = clientApp,
            ExternalReference = $"ref-{clientApp}",
            Provider = PaymentProviderType.Stripe,
            Amount = 25m,
            Currency = "GHS",
            Status = PaymentStatus.Succeeded,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        db.PaymentRecords.Add(payment);
        db.ClientCallbackDeliveries.Add(new ClientCallbackDelivery
        {
            Id = Guid.NewGuid(),
            PaymentRecordId = payment.Id,
            PaymentRecord = payment,
            EventType = "PaymentSucceeded",
            CallbackUrl = "https://client.test/callback",
            PayloadJson = "{}",
            Status = ClientCallbackStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        });
        db.SaveChanges();
        return payment;
    }

    private static ClientCallbackRetryWorker CreateWorker(
        PaymentsDbContext db,
        IClientCallbackDispatcher dispatcher,
        Dictionary<string, ClientCallbackRegistration> apps)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(dispatcher);
        services.AddSingleton<IOptions<ClientCallbackOptions>>(Options.Create(new ClientCallbackOptions { Apps = apps }));
        return new ClientCallbackRetryWorker(services.BuildServiceProvider(), NullLogger<ClientCallbackRetryWorker>.Instance);
    }
}
