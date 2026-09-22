using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AmazonRepricer.Infrastructure.Persistence;

public sealed class RepricerDbContextFactory
    : IDesignTimeDbContextFactory<RepricerDbContext>
{
    public RepricerDbContext CreateDbContext(string[] args)
    {
        var offline = args.Contains("--offline", StringComparer.Ordinal);

        // Offline model generation must not target an application database.
        var connectionString = offline
            ? "Host=127.0.0.1;Port=1;Database=repricer_design_time;"
              + "Username=design_time;Timeout=1"
            : Environment.GetEnvironmentVariable(
                "ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Set ConnectionStrings__DefaultConnection for database "
                + "operations, or pass --offline for migration generation.");
        }

        var options = new DbContextOptionsBuilder<RepricerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new RepricerDbContext(options);
    }
}
