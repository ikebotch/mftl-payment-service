using System.Text.Json.Serialization;

namespace MftlPaymentService.Dtos.v1.Request.Moolre;

public sealed class MoolreTransactionWebhookRequestDto
{
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("data")] public MoolreTransactionWebhookDataDto? Data { get; set; }
    [JsonPropertyName("go")] public string? Go { get; set; }
}

public sealed class MoolreTransactionWebhookDataDto
{
    [JsonPropertyName("txstatus")] public int TxStatus { get; set; }
    [JsonPropertyName("payer")] public string? Payer { get; set; }
    [JsonPropertyName("terminalid")] public string? TerminalId { get; set; }
    [JsonPropertyName("accountnumber")] public string? AccountNumber { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("amount")] public string? Amount { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("transactionid")] public string? TransactionId { get; set; }
    [JsonPropertyName("externalref")] public string? ExternalRef { get; set; }
    [JsonPropertyName("thirdpartyref")] public string? ThirdPartyRef { get; set; }
    [JsonPropertyName("secret")] public string? Secret { get; set; }
    [JsonPropertyName("ts")] public string? Ts { get; set; }
}
