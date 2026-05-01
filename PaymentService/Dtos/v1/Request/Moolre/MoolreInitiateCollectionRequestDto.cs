using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreInitiateCollectionRequestDto
{
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; }
    [JsonPropertyName("payer")] public string Payer { get; set; }
    [JsonPropertyName("channel")] public string Channel { get; set; }
    [JsonPropertyName("externalref")] public string ExtReference { get; set; }
    [JsonPropertyName("reference")] public string Reference { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("accountnumber")] public string AccountNumber { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; }
    [JsonPropertyName("callbackurl")] public string CallbackUrl { get; set; }
}