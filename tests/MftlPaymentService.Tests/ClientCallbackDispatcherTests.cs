using Microsoft.Extensions.Logging;
using Moq;
using MftlPaymentService.Data.Entities;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Tests.TestSupport;

namespace MftlPaymentService.Tests;

public sealed class ClientCallbackDispatcherTests
{
    private readonly Mock<ILogger<ClientCallbackDispatcher>> _loggerMock = new();

    [Fact]
    public async Task DispatchAsync_adds_signature_headers()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = TestHttpMessageHandler.Create((request, _) =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        });

        var dispatcher = new ClientCallbackDispatcher(client, _loggerMock.Object);
        await dispatcher.DispatchAsync(new ClientCallbackDelivery
        {
            CallbackUrl = "https://client.test/payments/callback",
            PayloadJson = """{"eventType":"PaymentSucceeded"}"""
        }, "shared-secret", CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.True(capturedRequest!.Headers.Contains("X-MFTL-Signature"));
        Assert.True(capturedRequest.Headers.Contains("X-MFTL-Timestamp"));
    }
}
