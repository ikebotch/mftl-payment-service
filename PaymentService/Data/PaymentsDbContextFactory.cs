using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MftlPaymentService.Data;

public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();

        var connectionString = Environment.GetEnvironmentVariable("PAYMENTS_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=mftl_payments;Username=mftl;Password=mftl_password;Include Error Detail=true";

        optionsBuilder.UseNpgsql(connectionString);

        return new PaymentsDbContext(optionsBuilder.Options);
    }
}
