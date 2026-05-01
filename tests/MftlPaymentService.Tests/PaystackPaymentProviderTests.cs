using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class PaystackPaymentProviderTests
{
    [Fact]
    public async Task CreatePaymentAsync_returns_authorization_url()
    {
        var client = TestHttpMessageHandler.Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/transaction/initialize", request.RequestUri!.ToString());
            return Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, """
                {
                  "status": true,
                  "data": {
                    "reference":"pst_ref_123",
                    "authorization_url":"https://checkout.paystack.test/authorize"
                  }
                }
                """));
        });

        var provider = new PaystackPaymentProvider(
            client,
            Options.Create(new PaystackSettings { SecretKey = "pk_test", CallbackUrl = "https://client.test/callback", DefaultCurrency = "GHS" }),
            NullLogger<PaystackPaymentProvider>.Instance);

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-123",
            Amount = 82m,
            Currency = "GHS",
            CustomerPhone = "+233241111111"
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("pst_ref_123", result.ProviderReference);
        Assert.Equal("https://checkout.paystack.test/authorize", result.CheckoutUrl);
    }

    [Fact]
    public async Task VerifyPaymentAsync_maps_success()
    {
        var client = TestHttpMessageHandler.Create((request, _) =>
            Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, """
                {
                  "status": true,
                  "data": {
                    "id":"123456",
                    "status":"success"
                  }
                }
                """)));

        var provider = new PaystackPaymentProvider(
            client,
            Options.Create(new PaystackSettings { SecretKey = "pk_test" }),
            NullLogger<PaystackPaymentProvider>.Instance);

        var result = await provider.VerifyPaymentAsync("pst_ref_123", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal("123456", result.ProviderTransactionId);
    }

    [Fact]
    public async Task ParseWebhookAsync_accepts_valid_signature()
    {
        var secret = "paystack_secret";
        var payload = """{"event":"charge.success","data":{"id":"tx_123","reference":"pst_ref_123","status":"success"}}""";
        var signature = WebhookHelpers.ComputeHmacSha512Hex(secret, payload);
        var context = TestFactory.CreateJsonRequest(payload, "x-paystack-signature", signature);
        var provider = new PaystackPaymentProvider(
            TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, "{}"))),
            Options.Create(new PaystackSettings { SecretKey = secret, WebhookSecret = secret }),
            NullLogger<PaystackPaymentProvider>.Instance);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
        Assert.Equal("pst_ref_123", result.ProviderReference);
    }

    [Fact]
    public async Task ParseWebhookAsync_rejects_invalid_signature()
    {
        var context = TestFactory.CreateJsonRequest("""{"event":"charge.success"}""", "x-paystack-signature", "bad");
        var provider = new PaystackPaymentProvider(
            TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(System.Net.HttpStatusCode.OK, "{}"))),
            Options.Create(new PaystackSettings { SecretKey = "secret" }),
            NullLogger<PaystackPaymentProvider>.Instance);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.False(result.SignatureValid);
    }
}
