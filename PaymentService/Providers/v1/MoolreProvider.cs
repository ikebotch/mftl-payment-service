using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Request.Moolre;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.Moolre;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Settings;
using RestSharp;

namespace MftlPaymentService.Providers.v1;

public class MoolreProvider : IMoolreProvider
{
    private IOptions<MoolreSettings> _settings;
    private ILogger<MoolreProvider> _logger;
    private RestClient _restClient;

    public MoolreProvider(IOptions<MoolreSettings> options, ILogger<MoolreProvider> logger)
    {
        _settings = options;
        _logger = logger;

        // configure rest client
        _restClient = new RestClient(_settings.Value.BaseUrl);
        _restClient.AddDefaultHeader("X-Api-Key", _settings.Value.ApiKey);
        _restClient.AddDefaultHeader("X-Api-User", _settings.Value.ApiUser);
    }

    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(
        InitiateCollectionRequestDto body)
    {
        try
        {
            // create a request
            var request = new RestRequest("/open/transact/topup");

            // create 
            var payload = new MoolreInitiateCollectionRequestDto
            {
                Amount = body.Amount,
                Payer = body.PhoneNumber,
                Channel = ParseCollectionChannel(body.Network),
                Currency = body.Currency,
                ExtReference = body.Reference,
                Reference = body.UserReference,
                Type = 1,
                AccountNumber = _settings.Value.PaymentAccountNumber,
                Username = _settings.Value.ApiUser
            };
            request.AddJsonBody(payload);

            // log request
            _logger.LogInformation($"InitiateCollection Request Payload: {JsonConvert.SerializeObject(payload)}");

            _logger.LogInformation("MOCK: Skipping actual Moolre call and returning success.");

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "success",
                Message = "Initiate collection was successful (MOCKED)",
                Data = new InitiateCollectionResponseDto
                {
                    Reference = $"MOCK-{Guid.NewGuid():N}"
                }
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(
        CompleteCollectionRequestDto body)
    {
        try
        {
            // create a request
            var request = new RestRequest("/open/transact/topup");

            var payload = new MoolreCompleteCollectionRequestDto
            {
                Amount = body.Amount,
                Payer = body.PhoneNumber,
                Channel = ParseCollectionChannel(body.Network),
                Currency = body.Currency,
                ExtReference = body.Reference,
                Otp = 2,
                OtpCode = body.Otp,
                Reference = body.UserReference,
                Type = 1,
                AccountNumber = _settings.Value.PaymentAccountNumber,
                Username = _settings.Value.ApiUser
            };
            request.AddJsonBody(payload);

            // log request
            _logger.LogInformation($"CompleteCollection Request Payload: {JsonConvert.SerializeObject(payload)}");

            // make request
            var response = await _restClient.PostAsync<MoolreServiceResponse>(request);

            // check for empty response
            if (response is null)
            {
                _logger.LogInformation("CompleteCollection Response: Returned a null response");

                return new ServiceResponseModel<CompleteCollectionResponseDto>
                {
                    Status = "failed",
                    Message = "Something went wrong"
                };
            }

            if (response.Status != 1)
            {
                _logger.LogInformation(
                    $"CompleteCollection Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<CompleteCollectionResponseDto>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            // check if otp failed
            if (response.Code == "TP15")
            {
                _logger.LogInformation(
                    $"CompleteCollection Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");

                return new ServiceResponseModel<CompleteCollectionResponseDto>
                {
                    Status = "failed",
                    Message = $"Invalid code. Please check and try again."
                };
            }

            // check for a successful verification 
            if (response.Code == "TP17")
            {
                _logger.LogInformation(
                    $"CompleteCollection Response: Returned a success response - {JsonConvert.SerializeObject(response)}");

                return new ServiceResponseModel<CompleteCollectionResponseDto>
                {
                    Status = "success",
                    Message = "Complete collection was successful",
                    Data = new CompleteCollectionResponseDto
                    {
                        Reference = response.Data
                    }
                };
            }

            _logger.LogInformation(
                $"CompleteCollection Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");

            return new ServiceResponseModel<CompleteCollectionResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<CompleteCollectionResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(string reference)
    {
        try
        {
            // create a request
            var request = new RestRequest("/open/transact/status");

            var payload = new MoolreCheckPaymentStatusRequestDto
            {
                Type = 1,
                IdType = 2,
                Id = reference,
                AccountNumber = _settings.Value.PaymentAccountNumber
            };
            request.AddJsonBody(payload);

            // log request
            _logger.LogInformation($"CheckPaymentStatus Request Payload: {JsonConvert.SerializeObject(payload)}");

            // make request
            var response =
                await _restClient.PostAsync<MoolreServiceResponse<MoolreCheckPaymentStatusResponseDto>>(request);

            // check for empty response
            if (response is null)
            {
                _logger.LogInformation("CheckPaymentStatus Response: Returned a null response");

                return new ServiceResponseModel<CheckPaymentStatusResponseDto>
                {
                    Status = "failed",
                    Message = "Something went wrong"
                };
            }

            if (response.Status != 1)
            {
                _logger.LogInformation(
                    $"CheckPaymentStatus Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<CheckPaymentStatusResponseDto>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            var status = "pending";

            // check for successful state
            if (response.Data.TxStatus == 1) status = "success";

            // check for successful state
            if (response.Data.TxStatus == 2) status = "failed";

            return new ServiceResponseModel<CheckPaymentStatusResponseDto>
            {
                Status = "success",
                Message = "Payment status check was successful",
                Data = new CheckPaymentStatusResponseDto
                {
                    Status = status
                }
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<CheckPaymentStatusResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body)
    {
        try
        {
            // create a request
            var request = new RestRequest("/open/transact/payout");

            var payload = new MoolreDisbursementRequestDto
            {
                Amount = body.Amount,
                Receiver = body.PhoneNumber,
                Channel = ParseDisbursementChannel(body.Network),
                Currency = body.Currency,
                ExtReference = body.Reference,
                Type = 1,
                AccountNumber = _settings.Value.PaymentAccountNumber,
                SublistId = body.BankBranch
            };
            request.AddJsonBody(payload);

            // log request
            _logger.LogInformation($"Disbursement Request Payload: {JsonConvert.SerializeObject(payload)}");

            // make request
            var response = await _restClient.PostAsync<MoolreServiceResponse>(request);

            // check for empty response
            if (response is null)
            {
                _logger.LogInformation("Disbursement Response: Returned a null response");

                return new ServiceResponseModel<DisbursementResponseDto>
                {
                    Status = "failed",
                    Message = "Something went wrong"
                };
            }

            if (response.Status != 1)
            {
                _logger.LogInformation(
                    $"Disbursement Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<DisbursementResponseDto>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            // check for a successful disbursement 
            if (response.Code != "OBGH01")
            {
                _logger.LogInformation(
                    $"Disbursement Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<DisbursementResponseDto>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            _logger.LogInformation(
                $"Disbursement Response: Returned a successful response - {JsonConvert.SerializeObject(response)}");

            return new ServiceResponseModel<DisbursementResponseDto>
            {
                Status = "success",
                Message = "Disbursement was successful",
                Data = new DisbursementResponseDto
                {
                    Reference = response.Reference ?? string.Empty
                }
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<DisbursementResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber,
        string network, string? bankBranch)
    {
        try
        {
            // create a request
            var request = new RestRequest("open/transact/validate");

            // create 
            var payload = new MoolreWalletNameCheckRequestDto
            {
                Type = 1,
                Channel = ParseDisbursementChannel(network),
                Currency = "GHS",
                Receiver = phoneNumber,
                AccountNumber = _settings.Value.PaymentAccountNumber,
                Sublistid = bankBranch
            };
            request.AddJsonBody(payload);

            // log request
            _logger.LogInformation($"WalletNameCheck Request Payload: {JsonConvert.SerializeObject(payload)}");

            // make request
            var response = await _restClient.PostAsync<MoolreServiceResponse>(request);

            // check for empty response
            if (response is null)
            {
                _logger.LogInformation("WalletNameCheck Response: Returned a null response");

                return new ServiceResponseModel<WalletNameCheckResponseDto>
                {
                    Status = "failed",
                    Message = "Something went wrong"
                };
            }

            if (response.Status != 1)
            {
                _logger.LogInformation(
                    $"WalletNameCheck Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<WalletNameCheckResponseDto>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            _logger.LogInformation(
                $"WalletNameCheck Response: Returned a successful response - {JsonConvert.SerializeObject(response)}");

            return new ServiceResponseModel<WalletNameCheckResponseDto>
            {
                Status = "success",
                Message = "Wallet name check was successful",
                Data = new WalletNameCheckResponseDto
                {
                    Name = response.Data
                }
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<WalletNameCheckResponseDto>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    public async Task<ServiceResponseModel<List<BankModel>>> GetBanks()
    {
        try
        {
            // create a request
            var request = new RestRequest("open/transact/data?data=banks&country=gha");

            // make request
            var response = await _restClient.PostAsync<MoolreServiceResponse<List<BankModel>>>(request);

            // check for empty response
            if (response is null)
            {
                _logger.LogInformation("GetBanks Response: Returned a null response");

                return new ServiceResponseModel<List<BankModel>>
                {
                    Status = "failed",
                    Message = "Something went wrong"
                };
            }

            if (response.Status != 1)
            {
                _logger.LogInformation(
                    $"GetBanks Response: Returned a non-success response - {JsonConvert.SerializeObject(response)}");
                return new ServiceResponseModel<List<BankModel>>
                {
                    Status = "failed",
                    Message = $"Something went wrong: {response.Message}"
                };
            }

            return new ServiceResponseModel<List<BankModel>>
            {
                Status = "success",
                Message = "Get banks were successful",
                Data = response.Data
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong: {e.Message}", e);

            return new ServiceResponseModel<List<BankModel>>
            {
                Status = "failed",
                Message = "Something went wrong"
            };
        }
    }

    private string ParseDisbursementChannel(string network)
    {
        string channel;

        switch (network)
        {
            case "mtn":
                channel = "1";
                break;

            case "at":
                channel = "7";
                break;

            case "telecel":
                channel = "6";
                break;

            case "bank":
                channel = "2";
                break;

            default:
                channel = "0";
                break;
        }

        return channel;
    }

    private string ParseCollectionChannel(string network)
    {
        string channel;

        switch (network)
        {
            case "mtn":
                channel = "13";
                break;

            case "at":
                channel = "7";
                break;

            case "telecel":
                channel = "6";
                break;

            default:
                channel = "0";
                break;
        }

        return channel;
    }
}