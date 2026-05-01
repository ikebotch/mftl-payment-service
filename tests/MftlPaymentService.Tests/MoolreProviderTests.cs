using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Providers.v1;
using MftlPaymentService.Settings;
using Moq;
using Xunit;

namespace MftlPaymentService.Tests;

public class MoolreProviderTests
{
    private readonly Mock<ILogger<MoolreProvider>> _loggerMock = new();

    [Fact]
    public async Task InitiateCollection_Should_Return_Mock_Reference_In_Mock_Mode()
    {
        // Arrange
        var options = Options.Create(new MoolreSettings
        {
            Mode = "Mock",
            BaseUrl = "https://api.moolre.com"
        });
        var provider = new MoolreProvider(options, _loggerMock.Object);
        var request = new InitiateCollectionRequestDto
        {
            Amount = 10,
            Currency = "GHS",
            PhoneNumber = "0244123456",
            Reference = "REF-123",
            UserReference = "USER-123",
            Network = "mtn"
        };

        // Act
        var result = await provider.InitiateCollection(request);

        // Assert
        Assert.Equal("success", result.Status);
        Assert.StartsWith("MOCK-", result.Data.Reference);
    }

    [Fact]
    public void Constructor_Should_Throw_If_Real_Mode_Missing_CallbackUrl()
    {
        // Arrange
        var options = Options.Create(new MoolreSettings
        {
            Mode = "Real",
            BaseUrl = "https://api.moolre.com",
            ApiKey = "key",
            ApiUser = "user"
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new MoolreProvider(options, _loggerMock.Object));
        Assert.Contains("public callback URL", ex.Message);
    }

    [Fact]
    public void Constructor_Should_Throw_If_Real_Mode_Has_Localhost_CallbackUrl()
    {
        // Arrange
        var options = Options.Create(new MoolreSettings
        {
            Mode = "Real",
            BaseUrl = "https://api.moolre.com",
            ApiKey = "key",
            ApiUser = "user",
            CallbackUrl = "http://localhost:5005/callback"
        });

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => new MoolreProvider(options, _loggerMock.Object));
        Assert.Contains("not localhost", ex.Message);
    }
}
