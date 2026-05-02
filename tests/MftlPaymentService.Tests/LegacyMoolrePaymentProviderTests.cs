using MftlPaymentService.Domain;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Providers.v1;
using Microsoft.Extensions.Options;
using MftlPaymentService.Settings;

namespace MftlPaymentService.Tests;

public sealed class LegacyMoolrePaymentProviderTests
{
    [Fact]
    public async Task CreatePaymentAsync_preserves_existing_behaviour()
    {
        var options = Options.Create(new MoolreSettings { Mode = "Mock" });
        var provider = new LegacyMoolrePaymentProvider(new StubMoolreProvider(), options);

        var result = await provider.CreatePaymentAsync(new CreateProviderPaymentRequest
        {
            ClientApp = "mftl-collections",
            ExternalReference = "COL-MOOLRE-1",
            Amount = 10m,
            Currency = "GHS",
            CustomerPhone = "+233241111111"
        }, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("moolre-ref", result.ProviderReference);
    }

    private sealed class StubMoolreProvider : IMoolreProvider
    {
        public Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(InitiateCollectionRequestDto body) =>
            Task.FromResult(new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "success",
                Message = "ok",
                Data = new InitiateCollectionResponseDto { Reference = "moolre-ref" }
            });

        public Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(CompleteCollectionRequestDto body) => throw new NotImplementedException();
        public Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(string reference) => throw new NotImplementedException();
        public Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body) => throw new NotImplementedException();
        public Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber, string network, string? bankBranch) => throw new NotImplementedException();
        public Task<ServiceResponseModel<List<BankModel>>> GetBanks() => throw new NotImplementedException();
    }
}
