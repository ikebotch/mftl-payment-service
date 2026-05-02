using Microsoft.Extensions.Options;
using System.Text.Json;
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

        var settings = _settings.Value;
        if (string.Equals(settings.Mode, "Real", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                throw new InvalidOperationException("Real Moolre requires a valid BaseUrl.");
            if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.ApiUser))
                throw new InvalidOperationException("Real Moolre requires valid credentials (ApiKey and ApiUser).");
            if (string.IsNullOrWhiteSpace(settings.CallbackUrl))
                throw new InvalidOperationException("Real Moolre requires a public callback URL.");
            if (settings.CallbackUrl.Contains("localhost") || settings.CallbackUrl.Contains("127.0.0.1"))
                throw new InvalidOperationException("Real Moolre requires a public callback URL, not localhost.");
            if (string.IsNullOrWhiteSpace(settings.WebhookSecret))
                throw new InvalidOperationException("Real Moolre requires a WebhookSecret for secure callback verification.");
        }

        // configure rest client
        var baseUrl = _settings.Value.BaseUrl?.Trim();
        var apiKey = _settings.Value.ApiKey?.Trim();
        var apiUser = _settings.Value.ApiUser?.Trim();

        _restClient = new RestClient(baseUrl ?? string.Empty);
        
        // Secure diagnostic log of config
        _logger.LogInformation("Moolre Provider Initialized. BaseUrl={BaseUrl}, X-Api-User={User}, X-Api-Key={Key}",
            baseUrl,
            Sanitize(apiUser),
            Sanitize(apiKey));
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(null)";
        if (value.Length <= 6) return new string('*', value.Length);
        return $"{value[..3]}...{value[^3..]} ({value.Length} chars)";
    }

    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(
        InitiateCollectionRequestDto body)
    {
        try
        {
            var isReal = string.Equals(_settings.Value.Mode, "Real", StringComparison.OrdinalIgnoreCase);

            // create 
            var payload = new MoolreInitiateCollectionRequestDto
            {
                Type = 1,
                Channel = ParseCollectionChannel(body.Network),
                Currency = body.Currency,
                Payer = body.PhoneNumber,
                Amount = body.Amount,
                ExtReference = body.Reference,
                Reference = body.UserReference,
                AccountNumber = _settings.Value.PaymentAccountNumber,
                Username = _settings.Value.ApiUser
            };

            // log request
            _logger.LogInformation("InitiateCollection Request: Mode={Mode}, Url={Url}, ExternalReference={ExtReference}, Amount={Amount} {Currency}, CallbackUrl={CallbackUrl}",
                _settings.Value.Mode, _settings.Value.BaseUrl, payload.ExtReference, payload.Amount, payload.Currency, _settings.Value.CallbackUrl);

            if (!isReal)
            {
                _logger.LogInformation("MOCK: Skipping actual Moolre call and returning success.");

                return new ServiceResponseModel<InitiateCollectionResponseDto>
                {
                    Status = "success",
                    Message = "MOCK SUCCESS",
                    Data = new InitiateCollectionResponseDto { Reference = $"MOCK-{Guid.NewGuid():N}" }
                };
            }

            var request = new RestRequest("/open/transact/topup", Method.Post);
            request.AddJsonBody(payload);


            var apiKey = _settings.Value.ApiKey?.Trim();
            var apiUser = _settings.Value.ApiUser?.Trim();
            var baseUrl = _settings.Value.BaseUrl?.Trim();

            // Add headers explicitly to request
            request.AddHeader("X-Api-Key", apiKey ?? string.Empty);
            request.AddHeader("X-Api-User", apiUser ?? string.Empty);
            request.AddHeader("Content-Type", "application/json");

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonBody = System.Text.Json.JsonSerializer.Serialize(payload, jsonOptions);
            
            _logger.LogInformation("Moolre Real Request: URL={Url}, Headers=[X-Api-User:{User}, X-Api-Key:{Key}], Body={Body}",
                $"{baseUrl}/open/transact/topup",
                apiUser,
                Sanitize(apiKey),
                jsonBody);

            _logger.LogInformation("Moolre CURL: curl -X POST \"{BaseUrl}/open/transact/topup\" " +
                "-H \"Content-Type: application/json\" " +
                "-H \"X-Api-User: {User}\" " +
                "-H \"X-Api-Key: {Key}\" " +
                "-d '{Body}'",
                baseUrl, 
                apiUser, 
                "MASKED-KEY", 
                jsonBody.Replace("'", "'\\''"));

            // make request
            var response = await _restClient.PostAsync<MoolreServiceResponse>(request);

            if (response is null)
            {
                _logger.LogWarning("Moolre Initiation returned null response.");
                return new ServiceResponseModel<InitiateCollectionResponseDto>
                {
                    Status = "failed",
                    Message = "No response from Moolre"
                };
            }

            _logger.LogInformation("Moolre Initiation Response: Status={Status}, Code={Code}, Message={Message}, Reference={Reference}",
                response.Status, response.Code, response.Message, response.Reference);

            if (response.Status != 1)
            {
                return new ServiceResponseModel<InitiateCollectionResponseDto>
                {
                    Status = "failed",
                    Message = response.Message ?? $"Moolre error code: {response.Code}"
                };
            }

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "success",
                Message = "Initiate collection was successful",
                Data = new InitiateCollectionResponseDto
                {
                    Reference = response.Reference ?? response.Data ?? string.Empty
                }
            };
        }
        catch (Exception e)
        {
            _logger.LogError($"Something went wrong during Moolre initiation: {e.Message}", e);

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

            // Add headers explicitly
            request.AddHeader("X-Api-Key", _settings.Value.ApiKey?.Trim() ?? string.Empty);
            request.AddHeader("X-Api-User", _settings.Value.ApiUser?.Trim() ?? string.Empty);
            request.AddHeader("Content-Type", "application/json");

            // log request
            _logger.LogInformation("CompleteCollection Request: URL={Url}, Headers=[X-Api-User:{User}, X-Api-Key:{Key}], Body={Body}",
                $"{_settings.Value.BaseUrl}/open/transact/topup",
                _settings.Value.ApiUser,
                Sanitize(_settings.Value.ApiKey),
                JsonConvert.SerializeObject(payload));

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
            var request = new RestRequest("/open/transact/status", Method.Post);

            var payload = new MoolreCheckPaymentStatusRequestDto
            {
                Type = 1,
                IdType = 2, // 2 for External Reference
                Id = reference,
                AccountNumber = _settings.Value.PaymentAccountNumber
            };
            request.AddJsonBody(payload);

            // Ensure headers are set on the request explicitly
            request.AddHeader("X-Api-Key", _settings.Value.ApiKey?.Trim() ?? string.Empty);
            request.AddHeader("X-Api-User", _settings.Value.ApiUser?.Trim() ?? string.Empty);
            request.AddHeader("Content-Type", "application/json");

            _logger.LogInformation("CheckPaymentStatus Request: URL={Url}, Headers=[X-Api-User:{User}, X-Api-Key:{Key}], Body={Body}",
                $"{_settings.Value.BaseUrl}/open/transact/status",
                _settings.Value.ApiUser,
                Sanitize(_settings.Value.ApiKey),
                JsonConvert.SerializeObject(payload));
            request.AddHeader("Content-Type", "application/json");

            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var jsonBody = System.Text.Json.JsonSerializer.Serialize(payload, jsonOptions);
            _logger.LogInformation("Moolre Status Outbound Body: {Body}", jsonBody);

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

        switch (network?.ToLowerInvariant())
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

        switch (network?.ToLowerInvariant())
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