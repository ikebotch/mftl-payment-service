namespace MftlPaymentService.Settings;

public sealed class ClientCallbackOptions
{
    public Dictionary<string, ClientCallbackRegistration> Apps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ClientCallbackRegistration
{
    public string SharedSecret { get; set; } = string.Empty;
    public string? DefaultCallbackUrl { get; set; }
}
