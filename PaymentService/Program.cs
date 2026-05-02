using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MftlPaymentService.Data;
using MftlPaymentService.Infrastructure.Callbacks;
using MftlPaymentService.Infrastructure.Providers;
using MftlPaymentService.Interfaces.v1;
using MftlPaymentService.Providers.v1;
using MftlPaymentService.Services;
using MftlPaymentService.Settings;
using MftlPaymentService.Services.v1;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddHttpClient();
builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpContextAccessor();

// add settings
builder.Services.Configure<MoolreSettings>(builder.Configuration.GetSection("Moolre"));
builder.Services.Configure<PaystackSettings>(builder.Configuration.GetSection("Paystack"));
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.Configure<GoCardlessSettings>(builder.Configuration.GetSection("PaymentProviders:GoCardless"));
builder.Services.Configure<MollieSettings>(builder.Configuration.GetSection("PaymentProviders:Mollie"));
builder.Services.Configure<PaymentWebhookSettings>(builder.Configuration.GetSection("PaymentWebhook"));
builder.Services.Configure<ClientCallbackOptions>(builder.Configuration.GetSection("ClientCallbacks"));

// add services
builder.Services.AddScoped<IMoolreProvider, MoolreProvider>();
builder.Services.AddScoped<IPaystackProvider, PaystackProvider>();
builder.Services.AddScoped<IStripeProvider, StripeProvider>();
builder.Services.AddScoped<IMobileMoneyService, MobileMoneyService>();
builder.Services.AddScoped<IPaymentJourneyService, PaymentJourneyService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<LegacyMoolrePaymentProvider>();
builder.Services.AddHttpClient<StripePaymentProvider>();
builder.Services.AddHttpClient<PaystackPaymentProvider>();
builder.Services.AddHttpClient<GoCardlessPaymentProvider>();
builder.Services.AddHttpClient<MolliePaymentProvider>();
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<StripePaymentProvider>());
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<PaystackPaymentProvider>());
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<LegacyMoolrePaymentProvider>());
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<GoCardlessPaymentProvider>());
builder.Services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<MolliePaymentProvider>());
builder.Services.AddScoped<IPaymentProviderResolver, PaymentProviderResolver>();
builder.Services.AddHttpClient<IClientCallbackDispatcher, ClientCallbackDispatcher>();
builder.Services.AddScoped<IPaymentOrchestrator, PaymentOrchestrator>();
builder.Services.AddHostedService<MftlPaymentService.Services.ClientCallbackRetryWorker>();

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", corsPolicyBuilder =>
    {
        corsPolicyBuilder
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("WWW-Authenticate");
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Startup Diagnostics for Internal Clients
using (var scope = app.Services.CreateScope())
{
    var options = scope.ServiceProvider.GetRequiredService<IOptions<ClientCallbackOptions>>().Value;
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Internal Authentication Configured. Allowed Clients: {Clients}", string.Join(", ", options.Apps.Keys));

    var moolreOptions = scope.ServiceProvider.GetRequiredService<IOptions<MoolreSettings>>().Value;
    logger.LogInformation("Moolre Configuration: Mode={Mode}, BaseUrl={Url}, CallbackUrl={CallbackUrl}, ApiUser={UserLen} chars, ApiKey={KeyLen} chars, WebhookSecret={SecretLen} chars", 
        moolreOptions.Mode, 
        moolreOptions.BaseUrl, 
        moolreOptions.CallbackUrl, 
        (moolreOptions.ApiUser?.Trim().Length ?? 0), 
        (moolreOptions.ApiKey?.Trim().Length ?? 0), 
        (moolreOptions.WebhookSecret?.Trim().Length ?? 0));
}

app.UseCors("AllowAll");
app.MapControllers();

app.Run();
