using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using MftlPaymentService.Filters;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Settings;
using Moq;
using System.Text;
using Xunit;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MftlPaymentService.Tests;

public class InternalAuthTests
{
    private readonly Mock<ILogger<InternalAuthAttribute>> _loggerMock = new();
    private readonly ClientCallbackOptions _options = new();

    public InternalAuthTests()
    {
        _options.Apps["mftl-collections"] = new ClientCallbackRegistration
        {
            SharedSecret = "test-secret-12345678901234567890"
        };
    }

    private ResourceExecutingContext CreateContext(
        string? clientApp = null,
        string? timestamp = null,
        string? signature = null,
        string body = "{}")
    {
        var httpContext = new DefaultHttpContext();
        if (clientApp != null) httpContext.Request.Headers["X-MFTL-Client-App"] = clientApp;
        if (timestamp != null) httpContext.Request.Headers["X-MFTL-Timestamp"] = timestamp;
        if (signature != null) httpContext.Request.Headers["X-MFTL-Signature"] = signature;

        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton(_loggerMock.Object);
        serviceCollection.AddSingleton(Options.Create(_options));
        httpContext.RequestServices = serviceCollection.BuildServiceProvider();

        return new ResourceExecutingContext(
            new ActionContext(httpContext, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());
    }

    [Fact]
    public async Task Should_Return_403_When_Headers_Missing()
    {
        var filter = new InternalAuthAttribute();
        var context = CreateContext();

        await filter.OnResourceExecutionAsync(context, () => Task.FromResult(new ResourceExecutedContext(context, new List<IFilterMetadata>())));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task Should_Return_403_When_Client_Unknown()
    {
        var filter = new InternalAuthAttribute();
        var context = CreateContext(clientApp: "unknown-app", timestamp: DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), signature: "any");

        await filter.OnResourceExecutionAsync(context, () => Task.FromResult(new ResourceExecutedContext(context, new List<IFilterMetadata>())));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("Unknown client app", result.Value?.ToString() ?? "");
    }

    [Fact]
    public async Task Should_Pass_When_Signature_Valid()
    {
        var filter = new InternalAuthAttribute();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var body = "{\"test\":true}";
        var secret = "test-secret-12345678901234567890";
        var signature = WebhookHelpers.ComputeHmacSha256Hex(secret, $"{timestamp}.{body}");

        var context = CreateContext(clientApp: "mftl-collections", timestamp: timestamp, signature: signature, body: body);

        bool nextCalled = false;
        await filter.OnResourceExecutionAsync(context, () => {
            nextCalled = true;
            return Task.FromResult(new ResourceExecutedContext(context, new List<IFilterMetadata>()));
        });

        Assert.True(nextCalled);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task Should_Return_403_When_Signature_Invalid()
    {
        var filter = new InternalAuthAttribute();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var body = "{\"test\":true}";
        var signature = "wrong-signature";

        var context = CreateContext(clientApp: "mftl-collections", timestamp: timestamp, signature: signature, body: body);

        await filter.OnResourceExecutionAsync(context, () => Task.FromResult(new ResourceExecutedContext(context, new List<IFilterMetadata>())));

        var result = Assert.IsType<ObjectResult>(context.Result);
        Assert.Equal(403, result.StatusCode);
        Assert.Contains("Invalid signature", result.Value?.ToString() ?? "");
    }

    [Fact]
    public void Configuration_Should_Bind_Correctly()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClientCallbacks:Apps:mftl-collections:SharedSecret"] = "test-secret",
                ["ClientCallbacks:Apps:mftl-collections:DefaultCallbackUrl"] = "http://localhost"
            })
            .Build();

        var options = new ClientCallbackOptions();
        config.GetSection("ClientCallbacks").Bind(options);

        Assert.True(options.Apps.ContainsKey("mftl-collections"));
        Assert.Equal("test-secret", options.Apps["mftl-collections"].SharedSecret);
    }
}
