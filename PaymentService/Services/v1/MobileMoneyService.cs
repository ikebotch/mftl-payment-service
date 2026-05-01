using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Providers.v1;

namespace MftlPaymentService.Services.v1;

public class MobileMoneyService : IMobileMoneyService
{
    private readonly IMoolreProvider _moolre;
    private readonly IPaystackProvider _paystack;
    private readonly IStripeProvider _stripe;
    private readonly string _provider;
    private readonly ILogger<MobileMoneyService> _logger;
    private readonly IActivityLogService _activityLog;

    public MobileMoneyService(
        IMoolreProvider moolreProvider,
        IPaystackProvider paystackProvider,
        IStripeProvider stripeProvider,
        IConfiguration config,
        ILogger<MobileMoneyService> logger,
        IActivityLogService activityLog)
    {
        _moolre = moolreProvider;
        _paystack = paystackProvider;
        _stripe = stripeProvider;
        _provider = (config["PaymentProvider:MobileMoney"] ?? string.Empty).Trim().ToLowerInvariant();
        _logger = logger;
        _activityLog = activityLog;
    }

    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(InitiateCollectionRequestDto body)
    {
        const string activity = "mobilemoney.collect.initiate";
        await SafeLog(activity, "received", "pending", request: body, reference: body.Reference);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.InitiateCollection(body),
                "paystack" => _paystack.InitiateCollection(body),
                "stripe" => _stripe.InitiateCollection(body),
                _ => Task.FromResult(new ServiceResponseModel<InitiateCollectionResponseDto>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                request: body,
                response: response,
                reference: response.Data?.Reference ?? body.Reference,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", request: body, reference: body.Reference, errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(CompleteCollectionRequestDto body)
    {
        const string activity = "mobilemoney.collect.complete";
        await SafeLog(activity, "received", "pending", request: body, reference: body.Reference);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.CompleteCollection(body),
                "paystack" => _paystack.CompleteCollection(body),
                "stripe" => _stripe.CompleteCollection(body),
                _ => Task.FromResult(new ServiceResponseModel<CompleteCollectionResponseDto>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                request: body,
                response: response,
                reference: response.Data?.Reference ?? body.Reference,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", request: body, reference: body.Reference, errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<CompleteCollectionResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(string reference)
    {
        const string activity = "mobilemoney.status.check";
        await SafeLog(activity, "received", "pending", request: new { reference }, reference: reference);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.CheckPaymentStatus(reference),
                "paystack" => _paystack.CheckPaymentStatus(reference),
                "stripe" => _stripe.CheckPaymentStatus(reference),
                _ => Task.FromResult(new ServiceResponseModel<CheckPaymentStatusResponseDto>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                request: new { reference },
                response: response,
                reference: reference,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", request: new { reference }, reference: reference, errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<CheckPaymentStatusResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body)
    {
        const string activity = "mobilemoney.disbursement";
        await SafeLog(activity, "received", "pending", request: body, reference: body.Reference);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.Disbursement(body),
                "paystack" => _paystack.Disbursement(body),
                "stripe" => _stripe.Disbursement(body),
                _ => Task.FromResult(new ServiceResponseModel<DisbursementResponseDto>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                request: body,
                response: response,
                reference: body.Reference,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", request: body, reference: body.Reference, errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<DisbursementResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber, string network, string? bankBranch)
    {
        const string activity = "mobilemoney.wallet.name_check";
        var request = new { phoneNumber, network, bankBranch };
        await SafeLog(activity, "received", "pending", request: request, reference: phoneNumber);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.WalletNameCheck(phoneNumber, network, bankBranch),
                "paystack" => _paystack.WalletNameCheck(phoneNumber, network, bankBranch),
                "stripe" => _stripe.WalletNameCheck(phoneNumber, network, bankBranch),
                _ => Task.FromResult(new ServiceResponseModel<WalletNameCheckResponseDto>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                request: request,
                response: response,
                reference: phoneNumber,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", request: request, reference: phoneNumber, errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<WalletNameCheckResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<List<BankModel>>> GetBanks()
    {
        const string activity = "mobilemoney.banks.get";
        await SafeLog(activity, "received", "pending", provider: _provider);

        try
        {
            var response = await (_provider switch
            {
                "moolre" => _moolre.GetBanks(),
                "paystack" => _paystack.GetBanks(),
                "stripe" => _stripe.GetBanks(),
                _ => Task.FromResult(new ServiceResponseModel<List<BankModel>>
                {
                    Status = "failed",
                    Message = "Unknown service provider"
                })
            });

            await SafeLog(activity, "provider_response", NormalizeStatus(response.Status),
                response: response,
                provider: _provider);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Something went wrong during {Activity}", activity);
            await SafeLog(activity, "failed", "failed", errorMessage: ex.Message, provider: _provider);

            return new ServiceResponseModel<List<BankModel>>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    private async Task SafeLog(
        string activity,
        string stage,
        string status,
        object? request = null,
        object? response = null,
        string? provider = null,
        string? reference = null,
        string? errorMessage = null)
    {
        try
        {
            await _activityLog.LogAsync(
                activity: activity,
                stage: stage,
                status: status,
                request: request,
                response: response,
                provider: provider,
                reference: reference,
                errorMessage: errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist activity log. activity={Activity}, stage={Stage}", activity, stage);
        }
    }

    private static string NormalizeStatus(string? status)
    {
        return string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) ? "success" :
            string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ? "failed" : "pending";
    }
}
