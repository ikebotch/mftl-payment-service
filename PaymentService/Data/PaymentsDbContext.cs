using Microsoft.EntityFrameworkCore;
using MftlPaymentService.Data.Entities;

namespace MftlPaymentService.Data;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationPayment> ApplicationPayments => Set<ApplicationPayment>();
    public DbSet<ApplicationPaymentStatusEvent> ApplicationPaymentStatusEvents => Set<ApplicationPaymentStatusEvent>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<PaymentRecord> PaymentRecords => Set<PaymentRecord>();
    public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents => Set<ProcessedWebhookEvent>();
    public DbSet<ClientCallbackDelivery> ClientCallbackDeliveries => Set<ClientCallbackDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationPayment>(builder =>
        {
            builder.ToTable("application_payments");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.PaymentReference).HasMaxLength(100).IsRequired();
            builder.Property(x => x.PaymentOptionId).HasMaxLength(50).IsRequired();
            builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.Amount).HasPrecision(18, 2);
            builder.Property(x => x.ProviderTransactionId).HasMaxLength(200);
            builder.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            builder.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");

            builder.HasIndex(x => x.PaymentReference).IsUnique();
            builder.HasIndex(x => x.ApplicationId);
            builder.HasIndex(x => new { x.ApplicationId, x.Verified });
        });

        modelBuilder.Entity<ApplicationPaymentStatusEvent>(builder =>
        {
            builder.ToTable("application_payment_status_events");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
            builder.Property(x => x.RawStatus).HasMaxLength(50).IsRequired();
            builder.Property(x => x.NormalizedStatus).HasMaxLength(20).IsRequired();
            builder.Property(x => x.ReceivedAtUtc).HasDefaultValueSql("NOW()");

            builder.HasOne(x => x.ApplicationPayment)
                .WithMany(x => x.StatusEvents)
                .HasForeignKey(x => x.ApplicationPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => x.ApplicationPaymentId);
            builder.HasIndex(x => x.ExternalId);
        });

        modelBuilder.Entity<ActivityLog>(builder =>
        {
            builder.ToTable("activity_logs");
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Activity).HasMaxLength(120).IsRequired();
            builder.Property(x => x.Stage).HasMaxLength(80).IsRequired();
            builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
            builder.Property(x => x.Provider).HasMaxLength(40);
            builder.Property(x => x.Reference).HasMaxLength(200);
            builder.Property(x => x.PaymentReference).HasMaxLength(100);
            builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("NOW()");

            builder.HasIndex(x => new { x.Activity, x.CreatedAtUtc });
            builder.HasIndex(x => x.Reference);
            builder.HasIndex(x => x.PaymentReference);
            builder.HasIndex(x => x.ApplicationId);
        });

        modelBuilder.Entity<PaymentRecord>(builder =>
        {
            builder.ToTable("payments");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.ClientApp).HasMaxLength(120).IsRequired();
            builder.Property(x => x.ContributionId);
            builder.Property(x => x.ExternalReference).HasMaxLength(200).IsRequired();
            builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(30).IsRequired();
            builder.Property(x => x.ProviderReference).HasMaxLength(200);
            builder.Property(x => x.ProviderTransactionId).HasMaxLength(200);
            builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            builder.Property(x => x.CustomerEmail).HasMaxLength(320);
            builder.Property(x => x.CustomerPhone).HasMaxLength(50);
            builder.Property(x => x.Description).HasMaxLength(500);
            builder.Property(x => x.CallbackUrl).HasMaxLength(1000);
            builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            builder.Property(x => x.FailureReason).HasMaxLength(1000);
            builder.Property(x => x.CheckoutUrl).HasMaxLength(1000);
            builder.Property(x => x.Amount).HasPrecision(18, 2);
            builder.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            builder.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            builder.HasIndex(x => new { x.ClientApp, x.ExternalReference }).IsUnique();
            builder.HasIndex(x => x.ProviderReference);
            builder.HasIndex(x => x.ProviderTransactionId);
        });

        modelBuilder.Entity<ProcessedWebhookEvent>(builder =>
        {
            builder.ToTable("processed_webhook_events");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Provider).HasConversion<string>().HasMaxLength(30).IsRequired();
            builder.Property(x => x.EventId).HasMaxLength(200);
            builder.Property(x => x.ProviderReference).HasMaxLength(200);
            builder.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
            builder.Property(x => x.Error).HasMaxLength(1000);
            builder.Property(x => x.ProcessedAt).HasDefaultValueSql("NOW()");
            builder.HasIndex(x => new { x.Provider, x.EventId }).IsUnique();
            builder.HasIndex(x => new { x.Provider, x.ProviderReference, x.PayloadHash });
            builder.HasOne(x => x.PaymentRecord)
                .WithMany(x => x.WebhookEvents)
                .HasForeignKey(x => x.PaymentRecordId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ClientCallbackDelivery>(builder =>
        {
            builder.ToTable("client_callback_deliveries");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.EventType).HasMaxLength(80).IsRequired();
            builder.Property(x => x.CallbackUrl).HasMaxLength(1000).IsRequired();
            builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            builder.Property(x => x.LastError).HasMaxLength(1000);
            builder.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            builder.HasIndex(x => new { x.PaymentRecordId, x.EventType }).IsUnique();
            builder.HasOne(x => x.PaymentRecord)
                .WithMany(x => x.CallbackDeliveries)
                .HasForeignKey(x => x.PaymentRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
