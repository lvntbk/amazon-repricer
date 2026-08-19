using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class PricingRuleConfiguration : IEntityTypeConfiguration<PricingRule>
{
    public void Configure(EntityTypeBuilder<PricingRule> builder)
    {
        builder.ToTable("pricing_rules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Strategy)
            .HasConversion<string>()
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.MinimumPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.MaximumPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.AdjustmentValue)
            .HasPrecision(18, 4);

        builder.Property(x => x.MinimumProfitPercentage)
            .HasPrecision(8, 4);

        builder.HasIndex(x => x.ProductId)
            .IsUnique();
    }
}
