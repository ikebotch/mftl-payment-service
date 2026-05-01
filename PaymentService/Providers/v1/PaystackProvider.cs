using System.Text.Json;
using Microsoft.Extensions.Options;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Settings;
using RestSharp;

namespace MftlPaymentService.Providers.v1;

public sealed class PaystackProvider : IPaystackProvider
{
    private readonly ILogger<PaystackProvider> _logger;
    private readonly RestClient _restClient;

    public PaystackProvider(IOptions<PaystackSettings> options, ILogger<PaystackProvider> logger)
    {
        _logger = logger;
        var settings = options.Value;
        _restClient = new RestClient(settings.BaseUrl.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(settings.SecretKey))
            _restClient.AddDefaultHeader("Authorization", $"Bearer {settings.SecretKey}");
        if (!string.IsNullOrWhiteSpace(settings.CallbackUrl))
            _restClient.AddDefaultHeader("X-Callback-Url", settings.CallbackUrl);
    }

    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(InitiateCollectionRequestDto body)
    {
        try
        {
            var request = new RestRequest("/transaction/initialize", Method.Post);
            var payload = new
            {
                email = BuildEmail(body.PhoneNumber),
                amount = ConvertToMinorUnits(body.Amount),
                currency = body.Currency?.ToUpperInvariant(),
                reference = body.Reference,
                metadata = new
                {
                    userReference = body.UserReference,
                    phoneNumber = body.PhoneNumber,
                    network = body.Network
                }
            };
            request.AddJsonBody(payload);

            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return Failed<InitiateCollectionResponseDto>($"Paystack initialize failed: {(int)response.StatusCode} {response.Content}");

            using var json = JsonDocument.Parse(response.Content);
            var root = json.RootElement;
            var ok = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
            if (!ok)
                return Failed<InitiateCollectionResponseDto>(root.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Failed" : "Failed");

            var reference = root.GetProperty("data").GetProperty("reference").GetString() ?? body.Reference;

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "success",
                Message = "Initiate collection was successful",
                Data = new InitiateCollectionResponseDto { Reference = reference }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack initiate collection failed");
            return Failed<InitiateCollectionResponseDto>("Something went wrong");
        }
    }

    public async Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(CompleteCollectionRequestDto body)
    {
        var status = await CheckPaymentStatus(body.Reference);
        if (status.Status == "success" && status.Data?.Status == "success")
        {
            return new ServiceResponseModel<CompleteCollectionResponseDto>
            {
                Status = "success",
                Message = "Complete collection was successful",
                Data = new CompleteCollectionResponseDto { Reference = body.Reference }
            };
        }

        return Failed<CompleteCollectionResponseDto>("Payment not completed");
    }

    public async Task<ServiceResponseModel<CheckPaymentStatusResponseDto>> CheckPaymentStatus(string reference)
    {
        try
        {
            var request = new RestRequest($"/transaction/verify/{Uri.EscapeDataString(reference)}", Method.Get);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return Failed<CheckPaymentStatusResponseDto>($"Paystack verify failed: {(int)response.StatusCode} {response.Content}");

            using var json = JsonDocument.Parse(response.Content);
            var root = json.RootElement;
            var ok = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
            if (!ok)
                return Failed<CheckPaymentStatusResponseDto>(root.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Failed" : "Failed");

            var providerStatus = root.GetProperty("data").GetProperty("status").GetString() ?? "pending";
            var mapped = providerStatus.ToLowerInvariant() switch
            {
                "success" => "success",
                "failed" or "abandoned" or "reversed" => "failed",
                _ => "pending"
            };

            return new ServiceResponseModel<CheckPaymentStatusResponseDto>
            {
                Status = "success",
                Message = "Payment status check was successful",
                Data = new CheckPaymentStatusResponseDto { Status = mapped }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack payment status check failed");
            return Failed<CheckPaymentStatusResponseDto>("Something went wrong");
        }
    }

    public Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body) =>
        Task.FromResult(Failed<DisbursementResponseDto>("Disbursement is not implemented for Paystack in this service."));

    public Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber, string network, string? bankBranch) =>
        Task.FromResult(Failed<WalletNameCheckResponseDto>("Wallet name check is not implemented for Paystack in this service."));

    public async Task<ServiceResponseModel<List<BankModel>>> GetBanks()
    {
        try
        {
            var request = new RestRequest("/bank", Method.Get);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return Failed<List<BankModel>>("Could not fetch banks.");

            using var json = JsonDocument.Parse(response.Content);
            var root = json.RootElement;
            var ok = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.True;
            if (!ok)
                return Failed<List<BankModel>>("Could not fetch banks.");

            var banks = new List<BankModel>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var code = item.TryGetProperty("code", out var c) ? c.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code))
                        banks.Add(new BankModel { Name = name!, Code = code! });
                }
            }

            return new ServiceResponseModel<List<BankModel>>
            {
                Status = "success",
                Message = "Banks fetched successfully",
                Data = banks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paystack get banks failed");
            return Failed<List<BankModel>>("Something went wrong");
        }
    }

    private static ServiceResponseModel<T> Failed<T>(string message) where T : class, new() =>
        new()
        {
            Status = "failed",
            Message = message,
            Data = new T()
        };

    private static int ConvertToMinorUnits(decimal amount) => (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static string BuildEmail(string phoneNumber)
    {
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? "customer@mftl.local" : $"{digits}@mftl.local";
    }
}
