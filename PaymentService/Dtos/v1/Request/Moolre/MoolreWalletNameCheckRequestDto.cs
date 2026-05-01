using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreWalletNameCheckRequestDto
{
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("channel")] public string Channel { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; }
    [JsonPropertyName("receiver")] public string Receiver { get; set; }
    [JsonPropertyName("accountnumber")] public string AccountNumber { get; set; }
    [JsonPropertyName("sublistid")] public string? Sublistid { get; set; }
}