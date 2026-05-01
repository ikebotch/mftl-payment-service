namespace MftlPaymentService.Settings;

public sealed class PaymentWebhookSettings
{
    public string RegistrationPaymentStatusUrl { get; set; } = "http://localhost:7201/api/v1/payments/webhooks/status";
}
