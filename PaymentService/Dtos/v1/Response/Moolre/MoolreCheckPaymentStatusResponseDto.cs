using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Response.Moolre;

public class MoolreCheckPaymentStatusResponseDto
{
    [JsonPropertyName("txstatus")] public int TxStatus { get; set; }
}