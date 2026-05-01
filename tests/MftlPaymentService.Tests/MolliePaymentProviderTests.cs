using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class MolliePaymentProviderTests
{
    [Fact]
    public async Task CreatePaymentAsync_sends_card_checkout_request()
    {
        string? requestBody = null;
        var client = TestHttpMessageHandler.Create(async (request, ct) =>
        {
            requestBody = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v2/payments", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer test_mollie_key", request.Headers.Authorization!.ToString());
            return TestHttpMessageHandler.Json(HttpStatusCode.Created, """
                {
                  "id": "tr_123",
                  "status": "open",
                  "_links": {
                    "checkout": {
                      "href": "https://www.mollie.com/checkout/select-method/tr_123"
                    }
                  }
                }
                """);
        });
        var provider = CreateProvider(client);

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "REF-MOLLIE-1",
            Amount = 10m,
            Currency = "EUR",
            Description = "Contribution REF-MOLLIE-1",
            Metadata = JsonSerializer.SerializeToElement(new
            {
                tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                contributionId = Guid.Parse("22222222-2222-2222-2222-222222222222")
            })
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("tr_123", result.ProviderReference);
        Assert.Equal("https://www.mollie.com/checkout/select-method/tr_123", result.CheckoutUrl);
        Assert.NotNull(requestBody);
        using var document = JsonDocument.Parse(requestBody!);
        var root = document.RootElement;
        Assert.Equal("creditcard", root.GetProperty("method").GetString());
        Assert.Equal("EUR", root.GetProperty("amount").GetProperty("currency").GetString());
        Assert.Equal("10.00", root.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("https://client.test/payments/mollie/return?externalReference=REF-MOLLIE-1", root.GetProperty("redirectUrl").GetString());
        Assert.Equal("https://hooks.test/callback/transactions/mollie", root.GetProperty("webhookUrl").GetString());
        Assert.Equal("REF-MOLLIE-1", root.GetProperty("metadata").GetProperty("externalReference").GetString());
        Assert.Equal("mftl-collections", root.GetProperty("metadata").GetProperty("clientApp").GetString());
    }

    [Fact]
    public async Task ParseWebhookAsync_rejects_missing_id()
    {
        var provider = CreateProvider();
        var context = TestFactory.CreateJsonRequest("{}");

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.False(result.SignatureValid);
        Assert.Equal("Missing Mollie payment id.", result.FailureReason);
    }

    [Fact]
    public async Task ParseWebhookAsync_fetches_payment_before_trusting_status()
    {
        var provider = CreateProvider(CreateFetchClient(FetchedPayment("tr_123", "failed")));
        var context = TestFactory.CreateJsonRequest("""{"id":"tr_123","status":"paid"}""");

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Equal("tr_123:failed", result.EventId);
    }

    [Theory]
    [InlineData("paid", PaymentStatus.Succeeded)]
    [InlineData("failed", PaymentStatus.Failed)]
    [InlineData("canceled", PaymentStatus.Cancelled)]
    [InlineData("expired", PaymentStatus.Cancelled)]
    [InlineData("open", PaymentStatus.Pending)]
    [InlineData("pending", PaymentStatus.Pending)]
    [InlineData("authorized", PaymentStatus.Pending)]
    public async Task ParseWebhookAsync_maps_fetched_status(string mollieStatus, PaymentStatus expectedStatus)
    {
        var provider = CreateProvider(CreateFetchClient(FetchedPayment("tr_123", mollieStatus)));
        var context = CreateFormRequest("id=tr_123");

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(expectedStatus, result.Status);
    }

    [Fact]
    public async Task CreatePaymentAsync_rejects_localhost_webhook_in_live_environment()
    {
        var provider = CreateProvider(
            TestHttpMessageHandler.Create((_, _) => throw new InvalidOperationException("HTTP should not be called.")),
            new MollieSettings
            {
                Enabled = true,
                Environment = "Live",
                ApiKey = "test_mollie_key",
                RedirectBaseUrl = "https://client.test/payments/mollie/return",
                WebhookBaseUrl = "https://localhost:5001",
                WebhookPath = "/callback/transactions/mollie",
                WebhookVerificationMode = "FetchPayment"
            });

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "REF-MOLLIE-LIVE",
            Amount = 10m,
            Currency = "EUR",
            Metadata = JsonSerializer.SerializeToElement(new { })
        }, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Mollie webhook URL must be public in Live environment.", result.FailureReason);
    }

    private static HttpClient CreateFetchClient(string paymentJson) =>
        TestHttpMessageHandler.Create((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v2/payments/tr_123", request.RequestUri!.AbsolutePath);
            return Task.FromResult(TestHttpMessageHandler.Json(HttpStatusCode.OK, paymentJson));
        });

    private static DefaultHttpContext CreateFormRequest(string body)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentLength = bytes.Length;
        context.Request.ContentType = "application/x-www-form-urlencoded";
        return context;
    }

    private static MolliePaymentProvider CreateProvider(HttpClient? client = null, MollieSettings? settings = null) =>
        new(
            client ?? TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))),
            Options.Create(settings ?? new MollieSettings
            {
                Enabled = true,
                Environment = "Test",
                ApiKey = "test_mollie_key",
                RedirectBaseUrl = "https://client.test/payments/mollie/return",
                WebhookBaseUrl = "https://hooks.test",
                WebhookPath = "/callback/transactions/mollie",
                WebhookVerificationMode = "FetchPayment"
            }),
            NullLogger<MolliePaymentProvider>.Instance);

    private static string FetchedPayment(string id, string status) =>
        $$"""
        {
          "id": "{{id}}",
          "status": "{{status}}",
          "amount": {
            "currency": "EUR",
            "value": "10.00"
          },
          "metadata": {
            "tenantId": "11111111-1111-1111-1111-111111111111",
            "contributionId": "22222222-2222-2222-2222-222222222222",
            "externalReference": "REF-MOLLIE-1",
            "clientApp": "mftl-collections"
          }
        }
        """;
}
