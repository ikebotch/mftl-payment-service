using Microsoft.AspNetCore.Http;
using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Tests.TestSupport;
using System.Text.Json;
using Moq;
using MftlPaymentService.Providers.v1;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Settings;
using Microsoft.Extensions.Options;

namespace MftlPaymentService.Tests;

public sealed class MoolreWebhookTests
{
    private static IOptions<MoolreSettings> CreateOptions(string mode = "Mock", string secret = "") =>
        Options.Create(new MoolreSettings { Mode = mode, WebhookSecret = secret });

    [Fact]
    public async Task ParseWebhookAsync_maps_txstatus_1_to_Succeeded()
    {
        var provider = new LegacyMoolrePaymentProvider(Mock.Of<IMoolreProvider>(), CreateOptions());
        var json = @"{
            ""data"": {
                ""externalref"": ""REF-123"",
                ""txstatus"": ""1"",
                ""transactionid"": ""TX-MOCK-1"",
                ""amount"": ""100.00""
            }
        }";
        var request = TestFactory.CreateJsonRequest(json);

        var result = await provider.ParseWebhookAsync(request.Request, CancellationToken.None);

        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal("REF-123", result.ProviderReference);
        Assert.Equal("TX-MOCK-1", result.ProviderTransactionId);
        Assert.Equal(100m, result.Amount);
    }

    [Fact]
    public async Task ParseWebhookAsync_maps_txstatus_1_to_Succeeded_official()
    {
        var provider = new LegacyMoolrePaymentProvider(Mock.Of<IMoolreProvider>(), CreateOptions());
        var json = @"{
            ""status"": 1,
            ""code"": ""TR099"",
            ""data"": {
                ""externalref"": ""REF-123"",
                ""txstatus"": ""1"",
                ""transactionid"": ""TX-MOCK-1""
            }
        }";
        var request = TestFactory.CreateJsonRequest(json);

        var result = await provider.ParseWebhookAsync(request.Request, CancellationToken.None);

        Assert.Equal(PaymentStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task Orchestrator_ProcessWebhookAsync_updates_payment_and_dispatches_callback()
    {
        await using var db = TestFactory.CreateDbContext();
        var moolreStub = new Mock<IMoolreProvider>();
        var provider = new LegacyMoolrePaymentProvider(moolreStub.Object, CreateOptions());
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var paymentId = Guid.NewGuid();
        var externalRef = "REF-d43c3c092b3a43729313511d76ee20c8";
        
        db.PaymentRecords.Add(new MftlPaymentService.Data.Entities.PaymentRecord
        {
            Id = paymentId,
            ClientApp = "mftl-collections",
            ExternalReference = externalRef,
            Provider = PaymentProviderType.Moolre,
            Status = PaymentStatus.Pending,
            Amount = 100m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var json = $@"{{
            ""data"": {{
                ""externalref"": ""{externalRef}"",
                ""txstatus"": ""1"",
                ""transactionid"": ""MOCK-TX-43187250"",
                ""thirdpartyref"": ""MOCK-THIRD-0000012187402663"",
                ""amount"": ""100.00""
            }}
        }}";
        var request = TestFactory.CreateJsonRequest(json);

        var outcome = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, request.Request, CancellationToken.None);

        Assert.True(outcome.Accepted);
        var payment = await db.PaymentRecords.FindAsync(paymentId);
        Assert.Equal(PaymentStatus.Succeeded, payment!.Status);
        Assert.Equal("MOCK-TX-43187250", payment.ProviderTransactionId);
        Assert.NotNull(payment.CompletedAt);

        // Verify callback delivery created
        var delivery = db.ClientCallbackDeliveries.Single();
        Assert.Equal(paymentId, delivery.PaymentRecordId);
        Assert.Equal("PaymentSucceeded", delivery.EventType);
    }

    [Fact]
    public async Task Moolre_callback_is_idempotent()
    {
        await using var db = TestFactory.CreateDbContext();
        var moolreStub = new Mock<IMoolreProvider>();
        var provider = new LegacyMoolrePaymentProvider(moolreStub.Object, CreateOptions());
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var externalRef = "REF-IDEMPOTENT";
        db.PaymentRecords.Add(new MftlPaymentService.Data.Entities.PaymentRecord
        {
            Id = Guid.NewGuid(),
            ClientApp = "mftl-collections",
            ExternalReference = externalRef,
            Provider = PaymentProviderType.Moolre,
            Status = PaymentStatus.Pending,
            Amount = 100m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var json = $@"{{
            ""data"": {{
                ""externalref"": ""{externalRef}"",
                ""txstatus"": ""1"",
                ""transactionid"": ""TX-123"",
                ""amount"": ""100.00""
            }}
        }}";
        
        var firstOutcome = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, TestFactory.CreateJsonRequest(json).Request, CancellationToken.None);
        var secondOutcome = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, TestFactory.CreateJsonRequest(json).Request, CancellationToken.None);

        Assert.True(firstOutcome.Accepted);
        Assert.True(secondOutcome.Duplicate);
        Assert.Single(db.ProcessedWebhookEvents);
        Assert.Single(db.ClientCallbackDeliveries);
    }

    [Fact]
    public async Task Unknown_externalref_does_not_throw()
    {
        await using var db = TestFactory.CreateDbContext();
        var moolreStub = new Mock<IMoolreProvider>();
        var provider = new LegacyMoolrePaymentProvider(moolreStub.Object, CreateOptions());
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var json = @"{
            ""data"": {
                ""externalref"": ""REF-UNKNOWN"",
                ""txstatus"": ""1"",
                ""transactionid"": ""TX-123""
            }
        }";
        var request = TestFactory.CreateJsonRequest(json);

        var outcome = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, request.Request, CancellationToken.None);

        Assert.True(outcome.Accepted);
        Assert.Null(outcome.Payment);
        var processedEvent = db.ProcessedWebhookEvents.Single();
        Assert.Equal(WebhookProcessingStatus.Failed, processedEvent.Status);
    }

    [Fact]
    public async Task Failed_txstatus_does_not_complete_payment()
    {
        await using var db = TestFactory.CreateDbContext();
        var moolreStub = new Mock<IMoolreProvider>();
        var provider = new LegacyMoolrePaymentProvider(moolreStub.Object, CreateOptions());
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var paymentId = Guid.NewGuid();
        db.PaymentRecords.Add(new MftlPaymentService.Data.Entities.PaymentRecord
        {
            Id = paymentId,
            ClientApp = "mftl-collections",
            ExternalReference = "REF-FAILED",
            Provider = PaymentProviderType.Moolre,
            Status = PaymentStatus.Pending,
            Amount = 100m,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var json = @"{
            ""data"": {
                ""externalref"": ""REF-FAILED"",
                ""txstatus"": ""2"",
                ""transactionid"": ""TX-FAILED""
            }
        }";
        var request = TestFactory.CreateJsonRequest(json);

        await orchestrator.ProcessWebhookAsync(PaymentProviderType.Moolre, request.Request, CancellationToken.None);

        var payment = await db.PaymentRecords.FindAsync(paymentId);
        Assert.Equal(PaymentStatus.Failed, payment!.Status);
        Assert.NotNull(payment.CompletedAt);
        
        var delivery = db.ClientCallbackDeliveries.Single();
        Assert.Equal("PaymentFailed", delivery.EventType);
    }

    [Fact]
    public async Task ParseWebhookAsync_fails_when_secret_mismatch_in_Real_mode()
    {
        var provider = new LegacyMoolrePaymentProvider(Mock.Of<IMoolreProvider>(), CreateOptions("Real", "correct-secret"));
        var json = @"{
            ""data"": {
                ""externalref"": ""REF-123"",
                ""secret"": ""wrong-secret"",
                ""txstatus"": ""1""
            }
        }";
        var request = TestFactory.CreateJsonRequest(json);

        var result = await provider.ParseWebhookAsync(request.Request, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal("Invalid Moolre webhook secret.", result.FailureReason);
    }
}
