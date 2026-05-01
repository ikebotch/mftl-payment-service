using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class StripePaymentProviderTests
{
    [Fact]
    public async Task CreatePaymentAsync_returns_checkout_session()
    {
        var client = TestHttpMessageHandler.Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/v1/checkout/sessions", request.RequestUri!.ToString());
            return Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, """
                {
                  "id":"cs_test_123",
                  "payment_intent":"pi_123",
                  "url":"https://checkout.stripe.test/session"
                }
                """));
        });

        var provider = new StripePaymentProvider(
            client,
            Options.Create(new StripeSettings
            {
                SecretKey = "sk_test",
                WebhookSecret = "whsec_test",
                SuccessUrl = "https://client.test/success",
                CancelUrl = "https://client.test/cancel",
                DefaultCurrency = "USD"
            }),
            NullLogger<StripePaymentProvider>.Instance);

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-123",
            Amount = 82m,
            Currency = "USD"
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("cs_test_123", result.ProviderReference);
        Assert.Equal("pi_123", result.ProviderTransactionId);
        Assert.Equal("https://checkout.stripe.test/session", result.CheckoutUrl);
    }

    [Fact]
    public async Task ParseWebhookAsync_accepts_valid_signature()
    {
        var secret = "whsec_test";
        var payload = """{"id":"evt_123","type":"checkout.session.completed","data":{"object":{"id":"cs_test_123","payment_intent":"pi_123"}}}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = WebhookHelpers.ComputeHmacSha256Hex(secret, $"{timestamp}.{payload}");
        var context = TestFactory.CreateJsonRequest(payload, "Stripe-Signature", $"t={timestamp},v1={signature}");

        var provider = new StripePaymentProvider(
            TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, "{}"))),
            Options.Create(new StripeSettings { SecretKey = "sk_test", WebhookSecret = secret }),
            NullLogger<StripePaymentProvider>.Instance);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal("evt_123", result.EventId);
    }

    [Fact]
    public async Task ParseWebhookAsync_rejects_invalid_signature()
    {
        var context = TestFactory.CreateJsonRequest("""{"id":"evt_123","type":"checkout.session.completed"}""", "Stripe-Signature", "t=1,v1=bad");
        var provider = new StripePaymentProvider(
            TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, "{}"))),
            Options.Create(new StripeSettings { SecretKey = "sk_test", WebhookSecret = "whsec_test" }),
            NullLogger<StripePaymentProvider>.Instance);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.False(result.SignatureValid);
    }
}
