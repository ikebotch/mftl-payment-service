using System.Security.Cryptography;
using System.Text;
using MftlPaymentService.Data.Entities;
using Microsoft.Extensions.Logging;

namespace MftlPaymentService.Infrastructure.Callbacks;

public sealed class ClientCallbackDispatcher(HttpClient httpClient, ILogger<ClientCallbackDispatcher> logger) : IClientCallbackDispatcher
{
    public async Task DispatchAsync(ClientCallbackDelivery delivery, string sharedSecret, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeSignature(sharedSecret, timestamp, delivery.PayloadJson);

        logger.LogInformation("Dispatching internal callback: Url={Url}, PaymentId={PaymentId}, EventType={EventType}", 
            delivery.CallbackUrl, delivery.PaymentRecordId, delivery.EventType);

        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.CallbackUrl)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-MFTL-Timestamp", timestamp);
        request.Headers.Add("X-MFTL-Signature", signature);

        try
        {
            using var response = await httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            
            logger.LogInformation("Internal callback response: StatusCode={StatusCode}, Body={Body}", 
                (int)response.StatusCode, body);
                
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to dispatch internal callback to {Url}", delivery.CallbackUrl);
            throw;
        }
    }

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
