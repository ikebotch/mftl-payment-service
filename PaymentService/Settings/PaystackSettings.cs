namespace MftlPaymentService.Settings;

public class PaystackSettings
{
    public string BaseUrl { get; set; } = "https://api.paystack.co";
    public string SecretKey { get; set; } = string.Empty;
    public string DefaultCurrency { get; set; } = "GHS";
    public string CallbackUrl { get; set; } = string.Empty;
    public string? WebhookSecret { get; set; }

    public string WebhookSecretOrSecretKey => string.IsNullOrWhiteSpace(WebhookSecret) ? SecretKey : WebhookSecret;
}
