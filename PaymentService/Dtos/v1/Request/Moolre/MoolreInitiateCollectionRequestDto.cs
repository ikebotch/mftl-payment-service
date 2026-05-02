using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreInitiateCollectionRequestDto
{
    [JsonPropertyName("type")] 
    [JsonProperty("type")]
    public int Type { get; set; } = 1;

    [JsonPropertyName("channel")] 
    [JsonProperty("channel")]
    public string Channel { get; set; }

    [JsonPropertyName("currency")] 
    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonPropertyName("payer")] 
    [JsonProperty("payer")]
    public string Payer { get; set; }

    [JsonPropertyName("amount")] 
    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("externalref")] 
    [JsonProperty("externalref")]
    public string ExtReference { get; set; }

    [JsonPropertyName("reference")] 
    [JsonProperty("reference")]
    public string Reference { get; set; }

    [JsonPropertyName("accountnumber")] 
    [JsonProperty("accountnumber")]
    public string AccountNumber { get; set; }

    [JsonPropertyName("username")] 
    [JsonProperty("username")]
    public string Username { get; set; }
}