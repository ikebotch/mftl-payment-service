namespace MftlPaymentService.Settings;

public sealed class GoCardlessSettings
{
    public bool Enabled { get; set; }
    public string Environment { get; set; } = "Sandbox";
    public string AccessToken { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string RedirectBaseUrl { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = "/callback/transactions/gocardless";
    public string ApiVersion { get; set; } = "2015-07-06";
    public string SandboxBaseUrl { get; set; } = "https://api-sandbox.gocardless.com";
    public string LiveBaseUrl { get; set; } = "https://api.gocardless.com";

    public string BaseUrl => Environment.Equals("Live", StringComparison.OrdinalIgnoreCase)
        ? LiveBaseUrl
        : SandboxBaseUrl;
}
