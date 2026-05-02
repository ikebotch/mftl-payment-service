namespace MftlPaymentService.Settings;

public class MoolreSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiUser { get; set; } = string.Empty;
    public string PaymentAccountNumber { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string Mode { get; set; } = "Mock"; // Mock or Real
    public string CallbackUrl { get; set; } = string.Empty;
}
