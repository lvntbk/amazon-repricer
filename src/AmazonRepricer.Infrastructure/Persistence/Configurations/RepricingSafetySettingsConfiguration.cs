using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class RepricingSafetySettingsConfiguration
    : IEntityTypeConfiguration<RepricingSafetySettings>
{
    public void Configure(
        EntityTypeBuilder<RepricingSafetySettings> builder)
    {
        builder.ToTable(
            "repricing_safety_settings",
            tableBuilder =>
            {
                tableBuilder.HasCheckConstraint(
                    "CK_repricing_safety_settings_singleton",
                    "\"Id\" = 1");

                tableBuilder.HasCheckConstraint(
                    "CK_repricing_safety_settings_max_price_change",
                    "\"MaxPriceChangePercentage\" > 0 AND " +
                    "\"MaxPriceChangePercentage\" <= 100");
            });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.PriceUpdatesEnabled)
            .IsRequired();

        builder.Property(x => x.MaxPriceChangePercentage)
            .HasPrecision(5, 2)
            .HasDefaultValue(10m)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();
    }
}
