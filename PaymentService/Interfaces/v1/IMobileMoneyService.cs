using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Models.v1;

namespace MftlPaymentService.Interfaces.v1;

public interface IMobileMoneyService
{
    public Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(
        InitiateCollectionRequestDto body);

    public Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(
        CompleteCollectionRequestDto body);

    public Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(string reference);
    public Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body);

    public Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber, string network,
        string? bankBranch);

    public Task<ServiceResponseModel<List<BankModel>>> GetBanks();
}