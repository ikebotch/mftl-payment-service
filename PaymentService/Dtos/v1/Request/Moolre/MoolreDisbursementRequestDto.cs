using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreDisbursementRequestDto
{
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; }
    [JsonPropertyName("receiver")] public string Receiver { get; set; }
    [JsonPropertyName("channel")] public string Channel { get; set; }
    [JsonPropertyName("externalref")] public string ExtReference { get; set; }
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("accountnumber")] public string AccountNumber { get; set; }
    [JsonPropertyName("sublistid")] public string? SublistId { get; set; }
}