namespace MftlPaymentService.Settings;

public class StripeSettings
{
    public string BaseUrl { get; set; } = "https://api.stripe.com";
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string DefaultCurrency { get; set; } = "USD";
    public string SuccessUrl { get; set; } = "https://example.com/payments/success";
    public string CancelUrl { get; set; } = "https://example.com/payments/cancel";
}
