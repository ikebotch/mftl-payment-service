using System.Security.Cryptography;
using System.Text;
using MftlPaymentService.Data.Entities;

namespace MftlPaymentService.Infrastructure.Callbacks;

public sealed class ClientCallbackDispatcher(HttpClient httpClient) : IClientCallbackDispatcher
{
    public async Task DispatchAsync(ClientCallbackDelivery delivery, string sharedSecret, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signature = ComputeSignature(sharedSecret, timestamp, delivery.PayloadJson);

        using var request = new HttpRequestMessage(HttpMethod.Post, delivery.CallbackUrl)
        {
            Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-MFTL-Timestamp", timestamp);
        request.Headers.Add("X-MFTL-Signature", signature);

        using var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
