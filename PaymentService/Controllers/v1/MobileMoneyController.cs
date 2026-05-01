using Commons.Filters;
using Microsoft.AspNetCore.Mvc;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Models.v1;

namespace MftlPaymentService.Controllers.v1;

[ApiController]
[Route("api/v1/mobilemoney")]
[ValidationFilter]
public class MobileMoneyController : Controller
{
    private readonly IMobileMoneyService _mobileMoneyService;

    public MobileMoneyController(IMobileMoneyService mobileMoneyService)
    {
        _mobileMoneyService = mobileMoneyService;
    }

    [HttpPost("collect/initiate")]
    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(
        [FromBody] InitiateCollectionRequestDto body)
    {
        return await _mobileMoneyService.InitiateCollection(body);
    }

    [HttpPost("collect/complete")]
    public async Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(
        [FromBody] CompleteCollectionRequestDto body)
    {
        return await _mobileMoneyService.CompleteCollection(body);
    }

    [HttpGet("tsq")]
    public async Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(
        [FromQuery] string reference)
    {
        return await _mobileMoneyService.CheckPaymentStatus(reference);
    }

    [HttpGet("disburse")]
    public async Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(
        [FromBody] DisbursementRequestDto body)
    {
        return await _mobileMoneyService.Disbursement(body);
    }

    [HttpGet("wallet/name-check")]
    public async Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck([FromQuery] string phoneNumber,
        [FromQuery] string network,
        [FromQuery] string? bankBranch)
    {
        return await _mobileMoneyService.WalletNameCheck(phoneNumber, network, bankBranch);
    }

    [HttpGet("banks")]
    public async Task<ServiceResponseModel<List<BankModel>>> GetBanks()
    {
        return await _mobileMoneyService.GetBanks();
    }
}