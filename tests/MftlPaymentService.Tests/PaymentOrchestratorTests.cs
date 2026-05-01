using MftlPaymentService.Contracts.Payments;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class PaymentOrchestratorTests
{
    [Fact]
    public async Task CreatePaymentAsync_is_idempotent_by_client_app_and_external_reference()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var request = new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-123",
            Provider = PaymentProviderType.Stripe,
            TenantId = Guid.NewGuid(),
            ContributionId = Guid.NewGuid(),
            Amount = 82m,
            Currency = "USD"
        };

        var first = await orchestrator.CreatePaymentAsync(request, CancellationToken.None);
        var second = await orchestrator.CreatePaymentAsync(request, CancellationToken.None);

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Single(db.PaymentRecords);
    }

    [Fact]
    public async Task ProcessWebhookAsync_ignores_duplicate_webhook_for_stripe()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-STRIPE-1",
            Provider = PaymentProviderType.Stripe,
            Amount = 82m
        }, CancellationToken.None);

        var request = TestFactory.CreateJsonRequest("{}");
        var first = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Stripe, request.Request, CancellationToken.None);
        var second = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Stripe, request.Request, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Duplicate);
        Assert.Single(db.ProcessedWebhookEvents);
    }

    [Fact]
    public async Task ProcessWebhookAsync_ignores_duplicate_webhook_for_paystack()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Paystack)
        {
            CreateResult = new CreatePaymentResult { Succeeded = true, ProviderReference = "pst_ref_1", CheckoutUrl = "https://paystack.test" },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = "paystack-event-1",
                ProviderReference = "pst_ref_1",
                Status = PaymentStatus.Succeeded,
                PayloadHash = "hash-1",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            }
        };

        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-PAYSTACK-1",
            Provider = PaymentProviderType.Paystack,
            Amount = 82m
        }, CancellationToken.None);

        var request = TestFactory.CreateJsonRequest("{}");
        var first = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Paystack, request.Request, CancellationToken.None);
        var second = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Paystack, request.Request, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Duplicate);
        Assert.Single(db.ProcessedWebhookEvents);
    }

    [Fact]
    public async Task VerifyPaymentAsync_sends_callback_once_for_success()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var created = await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-VERIFY-1",
            Provider = PaymentProviderType.Stripe,
            Amount = 82m
        }, CancellationToken.None);

        await orchestrator.VerifyPaymentAsync(created.PaymentId, CancellationToken.None);
        await orchestrator.VerifyPaymentAsync(created.PaymentId, CancellationToken.None);

        Assert.Equal(1, dispatcher.CallCount);
        Assert.Single(db.ClientCallbackDeliveries);
    }

    [Fact]
    public async Task VerifyPaymentAsync_retain_failed_callback_for_retry()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher { ThrowOnDispatch = true };
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        var created = await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-VERIFY-2",
            Provider = PaymentProviderType.Stripe,
            Amount = 82m
        }, CancellationToken.None);

        await orchestrator.VerifyPaymentAsync(created.PaymentId, CancellationToken.None);

        var callback = Assert.Single(db.ClientCallbackDeliveries);
        Assert.Equal(ClientCallbackStatus.Failed, callback.Status);
        Assert.Equal(1, callback.AttemptCount);
        Assert.NotNull(callback.LastError);
    }

    [Fact]
    public async Task VerifyPaymentAsync_callback_payload_includes_boundary_fields()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);
        var tenantId = Guid.NewGuid();
        var contributionId = Guid.NewGuid();

        var created = await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-CALLBACK-FIELDS",
            Provider = PaymentProviderType.Stripe,
            TenantId = tenantId,
            ContributionId = contributionId,
            Amount = 82m,
            Currency = "GHS"
        }, CancellationToken.None);

        await orchestrator.VerifyPaymentAsync(created.PaymentId, CancellationToken.None);

        Assert.NotNull(dispatcher.LastDelivery);
        using var document = System.Text.Json.JsonDocument.Parse(dispatcher.LastDelivery!.PayloadJson);
        var root = document.RootElement;
        Assert.Equal($"{created.PaymentId:N}:PaymentSucceeded", root.GetProperty("callbackEventId").GetString());
        Assert.Equal(created.PaymentId, root.GetProperty("paymentServicePaymentId").GetGuid());
        Assert.Equal(tenantId, root.GetProperty("tenantId").GetGuid());
        Assert.Equal(contributionId, root.GetProperty("contributionId").GetGuid());
        Assert.Equal("COL-CALLBACK-FIELDS", root.GetProperty("externalReference").GetString());
        Assert.Equal("PaymentSucceeded", root.GetProperty("eventType").GetString());
        Assert.True(root.TryGetProperty("occurredAt", out _));
    }

    [Fact]
    public async Task ProcessWebhookAsync_failed_after_success_does_not_dispatch_failure_callback()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.Stripe);
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-TERMINAL-1",
            Provider = PaymentProviderType.Stripe,
            TenantId = Guid.NewGuid(),
            ContributionId = Guid.NewGuid(),
            Amount = 82m
        }, CancellationToken.None);

        provider.WebhookResult = new WebhookParseResult
        {
            SignatureValid = true,
            EventId = "success-event",
            ProviderReference = "provider-ref",
            Status = PaymentStatus.Succeeded,
            PayloadHash = "hash-success",
            Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
        };
        await orchestrator.ProcessWebhookAsync(PaymentProviderType.Stripe, TestFactory.CreateJsonRequest("{}").Request, CancellationToken.None);

        provider.WebhookResult = new WebhookParseResult
        {
            SignatureValid = true,
            EventId = "failed-event",
            ProviderReference = "provider-ref",
            Status = PaymentStatus.Failed,
            PayloadHash = "hash-failed",
            FailureReason = "late failure",
            Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
        };
        await orchestrator.ProcessWebhookAsync(PaymentProviderType.Stripe, TestFactory.CreateJsonRequest("{}").Request, CancellationToken.None);

        Assert.Equal(1, dispatcher.CallCount);
        Assert.Single(db.ClientCallbackDeliveries);
        Assert.Equal(PaymentStatus.Succeeded, db.PaymentRecords.Single().Status);
    }
}
