using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public class MoolreCheckPaymentStatusRequestDto
{
    [JsonPropertyName("type")] public int Type { get; set; }
    [JsonPropertyName("idtype")] public int IdType { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; }
    [JsonPropertyName("accountnumber")] public string AccountNumber { get; set; }
}