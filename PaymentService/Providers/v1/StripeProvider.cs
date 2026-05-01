using System.Text.Json;
using Microsoft.Extensions.Options;
using MftlPaymentService.Dtos.v1.Request.MobileMoney;
using MftlPaymentService.Dtos.v1.Response.MobileMoney;
using MftlPaymentService.Models.v1;
using MftlPaymentService.Settings;
using RestSharp;

namespace MftlPaymentService.Providers.v1;

public sealed class StripeProvider : IStripeProvider
{
    private readonly ILogger<StripeProvider> _logger;
    private readonly RestClient _restClient;

    public StripeProvider(IOptions<StripeSettings> options, ILogger<StripeProvider> logger)
    {
        _logger = logger;
        var settings = options.Value;
        _restClient = new RestClient(settings.BaseUrl.TrimEnd('/'));
        if (!string.IsNullOrWhiteSpace(settings.SecretKey))
            _restClient.AddDefaultHeader("Authorization", $"Bearer {settings.SecretKey}");
    }

    public async Task<ServiceResponseModel<InitiateCollectionResponseDto>> InitiateCollection(InitiateCollectionRequestDto body)
    {
        try
        {
            var request = new RestRequest("/v1/payment_intents", Method.Post);
            request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
            request.AddParameter("amount", ConvertToMinorUnits(body.Amount));
            request.AddParameter("currency", body.Currency.ToLowerInvariant());
            request.AddParameter("description", $"Mobile money collection: {body.Reference}");
            request.AddParameter("metadata[reference]", body.Reference);
            request.AddParameter("metadata[userReference]", body.UserReference);
            request.AddParameter("metadata[phoneNumber]", body.PhoneNumber);
            request.AddParameter("automatic_payment_methods[enabled]", "true");

            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return Failed<InitiateCollectionResponseDto>($"Stripe payment_intent create failed: {(int)response.StatusCode} {response.Content}");

            using var json = JsonDocument.Parse(response.Content);
            var id = json.RootElement.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
                return Failed<InitiateCollectionResponseDto>("Stripe response missing payment intent id.");

            return new ServiceResponseModel<InitiateCollectionResponseDto>
            {
                Status = "success",
                Message = "Initiate collection was successful",
                Data = new InitiateCollectionResponseDto { Reference = id }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe initiate collection failed");
            return Failed<InitiateCollectionResponseDto>("Something went wrong");
        }
    }

    public async Task<ServiceResponseModel<CompleteCollectionResponseDto>> CompleteCollection(CompleteCollectionRequestDto body)
    {
        // For Stripe this service treats completion as status-based orchestration.
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
            var request = new RestRequest($"/v1/payment_intents/{Uri.EscapeDataString(reference)}", Method.Get);
            var response = await _restClient.ExecuteAsync(request);
            if (!response.IsSuccessful || string.IsNullOrWhiteSpace(response.Content))
                return Failed<CheckPaymentStatusResponseDto>($"Stripe payment_intent retrieve failed: {(int)response.StatusCode} {response.Content}");

            using var json = JsonDocument.Parse(response.Content);
            var providerStatus = json.RootElement.TryGetProperty("status", out var statusEl)
                ? statusEl.GetString() ?? "pending"
                : "pending";

            var mapped = providerStatus.ToLowerInvariant() switch
            {
                "succeeded" => "success",
                "canceled" => "failed",
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
            _logger.LogError(ex, "Stripe payment status check failed");
            return Failed<CheckPaymentStatusResponseDto>("Something went wrong");
        }
    }

    public Task<ServiceResponseModel<DisbursementResponseDto>> Disbursement(DisbursementRequestDto body) =>
        Task.FromResult(Failed<DisbursementResponseDto>("Disbursement is not implemented for Stripe in this service."));

    public Task<ServiceResponseModel<WalletNameCheckResponseDto>> WalletNameCheck(string phoneNumber, string network, string? bankBranch) =>
        Task.FromResult(Failed<WalletNameCheckResponseDto>("Wallet name check is not implemented for Stripe in this service."));

    public Task<ServiceResponseModel<List<BankModel>>> GetBanks() =>
        Task.FromResult(new ServiceResponseModel<List<BankModel>>
        {
            Status = "success",
            Message = "No bank list for Stripe",
            Data = new List<BankModel>()
        });

    private static ServiceResponseModel<T> Failed<T>(string message) where T : class, new() =>
        new()
        {
            Status = "failed",
            Message = message,
            Data = new T()
        };

    private static int ConvertToMinorUnits(decimal amount) => (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
