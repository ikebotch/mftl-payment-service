namespace MftlPaymentService.Settings;

public sealed class MollieSettings
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Test";
    public string ApiKey { get; set; } = string.Empty;
    public string RedirectBaseUrl { get; set; } = string.Empty;
    public string WebhookBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/callback/transactions/mollie";
    public string WebhookVerificationMode { get; set; } = "FetchPayment";
    public string BaseUrl { get; set; } = "https://api.mollie.com";
}
