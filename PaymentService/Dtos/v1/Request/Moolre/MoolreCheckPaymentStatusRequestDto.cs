using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreCheckPaymentStatusRequestDto
{
    [JsonPropertyName("type")] 
    [JsonProperty("type")]
    public int Type { get; set; } = 1;

    [JsonPropertyName("idtype")] 
    [JsonProperty("idtype")]
    public int IdType { get; set; } = 2; // 2 for External Reference

    [JsonPropertyName("id")] 
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonPropertyName("accountnumber")] 
    [JsonProperty("accountnumber")]
    public string AccountNumber { get; set; }
}