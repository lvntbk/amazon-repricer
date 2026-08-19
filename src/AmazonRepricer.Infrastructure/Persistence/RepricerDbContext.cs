using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Infrastructure.Persistence;

public sealed class RepricerDbContext : DbContext
{
    public RepricerDbContext(DbContextOptions<RepricerDbContext> options)
        : base(options)
    {
    }

    public DbSet<AmazonStore> AmazonStores => Set<AmazonStore>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();
    public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
    public DbSet<RepricingEvent> RepricingEvents => Set<RepricingEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RepricerDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
