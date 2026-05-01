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

    [Fact]
    public async Task ProcessWebhookAsync_gocardless_success_marks_payment_succeeded_and_creates_one_callback()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.GoCardless)
        {
            CreateResult = new CreatePaymentResult
            {
                Succeeded = true,
                ProviderReference = "BRQ123",
                CheckoutUrl = "https://pay-sandbox.gocardless.test/flow"
            },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = "EV123",
                ProviderReference = "BRQ123",
                ProviderTransactionId = "PM123",
                Status = PaymentStatus.Succeeded,
                PayloadHash = "gc-hash-success",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            }
        };
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-GC-1",
            Provider = PaymentProviderType.GoCardless,
            TenantId = Guid.NewGuid(),
            ContributionId = Guid.NewGuid(),
            Amount = 82m,
            Currency = "GBP"
        }, CancellationToken.None);

        var first = await orchestrator.ProcessWebhookAsync(PaymentProviderType.GoCardless, TestFactory.CreateJsonRequest("{}").Request, CancellationToken.None);
        var second = await orchestrator.ProcessWebhookAsync(PaymentProviderType.GoCardless, TestFactory.CreateJsonRequest("{}").Request, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Duplicate);
        Assert.Equal(PaymentStatus.Succeeded, db.PaymentRecords.Single().Status);
        Assert.Equal("PM123", db.PaymentRecords.Single().ProviderTransactionId);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Single(db.ClientCallbackDeliveries);
        Assert.Single(db.ProcessedWebhookEvents);
    }

    [Fact]
    public async Task ProcessWebhookAsync_gocardless_failed_marks_payment_failed()
    {
        await using var db = TestFactory.CreateDbContext();
        var provider = new FakeProvider(PaymentProviderType.GoCardless)
        {
            CreateResult = new CreatePaymentResult
            {
                Succeeded = true,
                ProviderReference = "BRQ456",
                CheckoutUrl = "https://pay-sandbox.gocardless.test/flow"
            },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = "EV456",
                ProviderReference = "BRQ456",
                ProviderTransactionId = "PM456",
                Status = PaymentStatus.Failed,
                FailureReason = "Payment failed",
                PayloadHash = "gc-hash-failed",
                Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            }
        };
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-GC-FAILED",
            Provider = PaymentProviderType.GoCardless,
            Amount = 82m,
            Currency = "GBP"
        }, CancellationToken.None);

        var result = await orchestrator.ProcessWebhookAsync(PaymentProviderType.GoCardless, TestFactory.CreateJsonRequest("{}").Request, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(PaymentStatus.Failed, db.PaymentRecords.Single().Status);
        Assert.Equal("Payment failed", db.PaymentRecords.Single().FailureReason);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Single(db.ClientCallbackDeliveries);
    }

    [Fact]
    public async Task ProcessWebhookAsync_mollie_paid_marks_payment_succeeded_and_creates_one_callback()
    {
        await using var db = TestFactory.CreateDbContext();
        var tenantId = Guid.NewGuid();
        var contributionId = Guid.NewGuid();
        var provider = new FakeProvider(PaymentProviderType.Mollie)
        {
            CreateResult = new CreatePaymentResult
            {
                Succeeded = true,
                ProviderReference = "tr_paid",
                CheckoutUrl = "https://mollie.test/checkout"
            },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = "tr_paid:paid",
                ProviderReference = "tr_paid",
                ProviderTransactionId = "tr_paid",
                Status = PaymentStatus.Succeeded,
                Amount = 10m,
                Currency = "EUR",
                PayloadHash = "mollie-paid-hash",
                Payload = MolliePayload(tenantId, contributionId, "REF-MOLLIE-PAID")
            }
        };
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "REF-MOLLIE-PAID",
            Provider = PaymentProviderType.Mollie,
            TenantId = tenantId,
            ContributionId = contributionId,
            Amount = 10m,
            Currency = "EUR"
        }, CancellationToken.None);

        var first = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Mollie, TestFactory.CreateJsonRequest("""{"id":"tr_paid"}""").Request, CancellationToken.None);
        var second = await orchestrator.ProcessWebhookAsync(PaymentProviderType.Mollie, TestFactory.CreateJsonRequest("""{"id":"tr_paid"}""").Request, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Duplicate);
        Assert.Equal(PaymentStatus.Succeeded, db.PaymentRecords.Single().Status);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.Single(db.ClientCallbackDeliveries);
        Assert.Single(db.ProcessedWebhookEvents);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Cancelled)]
    public async Task ProcessWebhookAsync_mollie_failure_statuses_create_failed_callback_not_success(PaymentStatus status)
    {
        await using var db = TestFactory.CreateDbContext();
        var tenantId = Guid.NewGuid();
        var contributionId = Guid.NewGuid();
        var provider = new FakeProvider(PaymentProviderType.Mollie)
        {
            CreateResult = new CreatePaymentResult
            {
                Succeeded = true,
                ProviderReference = "tr_failed",
                CheckoutUrl = "https://mollie.test/checkout"
            },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = $"tr_failed:{status}",
                ProviderReference = "tr_failed",
                ProviderTransactionId = "tr_failed",
                Status = status,
                Amount = 10m,
                Currency = "EUR",
                PayloadHash = $"mollie-{status}-hash",
                Payload = MolliePayload(tenantId, contributionId, "REF-MOLLIE-FAILED")
            }
        };
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "REF-MOLLIE-FAILED",
            Provider = PaymentProviderType.Mollie,
            TenantId = tenantId,
            ContributionId = contributionId,
            Amount = 10m,
            Currency = "EUR"
        }, CancellationToken.None);

        await orchestrator.ProcessWebhookAsync(PaymentProviderType.Mollie, TestFactory.CreateJsonRequest("""{"id":"tr_failed"}""").Request, CancellationToken.None);

        var delivery = Assert.Single(db.ClientCallbackDeliveries);
        Assert.Equal("PaymentFailed", delivery.EventType);
        Assert.Equal(1, dispatcher.CallCount);
        Assert.DoesNotContain("PaymentSucceeded", delivery.PayloadJson);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(null)]
    public async Task ProcessWebhookAsync_mollie_non_terminal_statuses_do_not_call_collections(PaymentStatus? status)
    {
        await using var db = TestFactory.CreateDbContext();
        var tenantId = Guid.NewGuid();
        var contributionId = Guid.NewGuid();
        var provider = new FakeProvider(PaymentProviderType.Mollie)
        {
            CreateResult = new CreatePaymentResult
            {
                Succeeded = true,
                ProviderReference = "tr_pending",
                CheckoutUrl = "https://mollie.test/checkout"
            },
            WebhookResult = new WebhookParseResult
            {
                SignatureValid = true,
                EventId = "tr_pending:pending",
                ProviderReference = "tr_pending",
                ProviderTransactionId = "tr_pending",
                Status = status,
                Amount = 10m,
                Currency = "EUR",
                PayloadHash = "mollie-pending-hash",
                Payload = MolliePayload(tenantId, contributionId, "REF-MOLLIE-PENDING")
            }
        };
        var dispatcher = new RecordingCallbackDispatcher();
        var orchestrator = TestFactory.CreateOrchestrator(db, dispatcher, provider);

        await orchestrator.CreatePaymentAsync(new CreatePaymentRequestDto
        {
            ClientApp = "mftl-collections",
            ExternalReference = "REF-MOLLIE-PENDING",
            Provider = PaymentProviderType.Mollie,
            TenantId = tenantId,
            ContributionId = contributionId,
            Amount = 10m,
            Currency = "EUR"
        }, CancellationToken.None);

        await orchestrator.ProcessWebhookAsync(PaymentProviderType.Mollie, TestFactory.CreateJsonRequest("""{"id":"tr_pending"}""").Request, CancellationToken.None);

        Assert.Equal(0, dispatcher.CallCount);
        Assert.Empty(db.ClientCallbackDeliveries);
    }

    private static System.Text.Json.JsonElement MolliePayload(Guid tenantId, Guid contributionId, string externalReference) =>
        System.Text.Json.JsonSerializer.SerializeToElement(new
        {
            metadata = new
            {
                tenantId,
                contributionId,
                externalReference,
                clientApp = "mftl-collections"
            }
        });
}
