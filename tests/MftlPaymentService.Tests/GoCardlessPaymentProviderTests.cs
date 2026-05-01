using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MftlPaymentService.Domain;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class GoCardlessPaymentProviderTests
{
    [Fact]
    public async Task CreatePaymentAsync_creates_billing_request_and_flow()
    {
        var calls = new List<(HttpMethod Method, string Path, string Body)>();
        var client = TestHttpMessageHandler.Create(async (request, ct) =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
            calls.Add((request.Method, request.RequestUri!.AbsolutePath, body));

            Assert.Equal("Bearer gc_test_token", request.Headers.Authorization!.ToString());
            Assert.True(request.Headers.TryGetValues("GoCardless-Version", out var versions));
            Assert.Equal("2015-07-06", versions.Single());

            return request.RequestUri.AbsolutePath switch
            {
                "/billing_requests" => TestHttpMessageHandler.Json(HttpStatusCode.Created, """
                    {
                      "billing_requests": {
                        "id": "BRQ123",
                        "status": "pending",
                        "payment_request": {
                          "amount": 8250,
                          "currency": "GBP"
                        },
                        "links": {}
                      }
                    }
                    """),
                "/billing_request_flows" => TestHttpMessageHandler.Json(HttpStatusCode.Created, """
                    {
                      "billing_request_flows": {
                        "id": "BRF123",
                        "authorisation_url": "https://pay-sandbox.gocardless.com/billing/static/flow?id=BRF123"
                      }
                    }
                    """),
                _ => TestHttpMessageHandler.Json(HttpStatusCode.NotFound, "{}")
            };
        });

        var provider = CreateProvider(client);

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-GC-123",
            Amount = 82.50m,
            Currency = "GBP",
            CustomerEmail = "donor@example.test",
            Description = "Contribution REF-123",
            Metadata = JsonSerializer.SerializeToElement(new
            {
                tenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                contributionId = Guid.Parse("22222222-2222-2222-2222-222222222222")
            })
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("BRQ123", result.ProviderReference);
        Assert.Equal("https://pay-sandbox.gocardless.com/billing/static/flow?id=BRF123", result.CheckoutUrl);
        Assert.Equal(2, calls.Count);

        using var billingRequest = JsonDocument.Parse(calls[0].Body);
        var paymentRequest = billingRequest.RootElement.GetProperty("billing_requests").GetProperty("payment_request");
        Assert.Equal(8250, paymentRequest.GetProperty("amount").GetInt32());
        Assert.Equal("GBP", paymentRequest.GetProperty("currency").GetString());
        Assert.Equal("COL-GC-123", paymentRequest.GetProperty("reference").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", paymentRequest.GetProperty("metadata").GetProperty("tenantId").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", paymentRequest.GetProperty("metadata").GetProperty("contributionId").GetString());

        using var flowRequest = JsonDocument.Parse(calls[1].Body);
        var flow = flowRequest.RootElement.GetProperty("billing_request_flows");
        Assert.Contains("externalReference=COL-GC-123", flow.GetProperty("redirect_uri").GetString());
        Assert.Equal("BRQ123", flow.GetProperty("links").GetProperty("billing_request").GetString());
        Assert.Equal("donor@example.test", flow.GetProperty("prefilled_customer").GetProperty("email").GetString());
    }

    [Fact]
    public async Task ParseWebhookAsync_accepts_valid_signature()
    {
        var secret = "gc_webhook_secret";
        var payload = SuccessfulWebhook();
        var signature = WebhookHelpers.ComputeHmacSha256Hex(secret, payload);
        var context = TestFactory.CreateJsonRequest(payload, "Webhook-Signature", signature);
        var provider = CreateProvider(webhookSecret: secret);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal("EV123", result.EventId);
        Assert.Equal("payments.confirmed", result.EventType);
        Assert.Equal("BRQ123", result.ProviderReference);
        Assert.Equal("PM123", result.ProviderTransactionId);
        Assert.Equal(PaymentStatus.Succeeded, result.Status);
    }

    [Fact]
    public async Task ParseWebhookAsync_rejects_invalid_signature()
    {
        var context = TestFactory.CreateJsonRequest(SuccessfulWebhook(), "Webhook-Signature", "bad");
        var provider = CreateProvider();

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.False(result.SignatureValid);
    }

    [Fact]
    public async Task ParseWebhookAsync_maps_failed_payment()
    {
        var secret = "gc_webhook_secret";
        var payload = """
            {
              "events": [
                {
                  "id": "EV456",
                  "action": "failed",
                  "resource_type": "payments",
                  "links": {
                    "payment": "PM456",
                    "billing_request": "BRQ456"
                  },
                  "details": {
                    "description": "Payment failed"
                  }
                }
              ]
            }
            """;
        var signature = WebhookHelpers.ComputeHmacSha256Hex(secret, payload);
        var context = TestFactory.CreateJsonRequest(payload, "Webhook-Signature", signature);
        var provider = CreateProvider(webhookSecret: secret);

        var result = await provider.ParseWebhookAsync(context.Request, CancellationToken.None);

        Assert.True(result.SignatureValid);
        Assert.Equal(PaymentStatus.Failed, result.Status);
        Assert.Equal("Payment failed", result.FailureReason);
    }

    private static GoCardlessPaymentProvider CreateProvider(HttpClient? client = null, string webhookSecret = "gc_webhook_secret") =>
        new(
            client ?? TestHttpMessageHandler.Create((_, _) => Task.FromResult(TestHttpMessageHandler.Json(HttpStatusCode.OK, "{}"))),
            Options.Create(new GoCardlessSettings
            {
                Enabled = true,
                Environment = "Sandbox",
                AccessToken = "gc_test_token",
                WebhookSecret = webhookSecret,
                RedirectBaseUrl = "https://client.test/gocardless/return"
            }),
            NullLogger<GoCardlessPaymentProvider>.Instance);

    private static string SuccessfulWebhook() =>
        """
        {
          "events": [
            {
              "id": "EV123",
              "action": "confirmed",
              "resource_type": "payments",
              "links": {
                "payment": "PM123",
                "billing_request": "BRQ123"
              }
            }
          ],
          "meta": {
            "webhook_id": "WB123"
          }
        }
        """;
}
