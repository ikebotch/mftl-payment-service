using System.Text.Json.Serialization;

namespace MftlPaymentService.Models.v1;

public class MoolreServiceResponse<T>
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("data")] public T Data { get; set; }
    [JsonPropertyName("go")] public string Go { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}

public class MoolreServiceResponse
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("code")] public string Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; }
    [JsonPropertyName("data")] public string Data { get; set; }
    [JsonPropertyName("go")] public string Go { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
}