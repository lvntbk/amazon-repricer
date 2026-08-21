using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class RepricingEventConfiguration
    : IEntityTypeConfiguration<RepricingEvent>
{
    public void Configure(EntityTypeBuilder<RepricingEvent> builder)
    {
        builder.ToTable("repricing_events");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OldPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.ProposedPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.AppliedPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(RepricingStatus.Pending)
            .IsRequired();

        builder.Property(x => x.ReviewNote)
            .HasMaxLength(1000);

        builder.HasIndex(x => new
        {
            x.ProductId,
            x.CreatedAtUtc
        });

        builder.HasIndex(x => new
        {
            x.Status,
            x.CreatedAtUtc
        });

        builder.HasOne(x => x.Product)
            .WithMany(x => x.RepricingEvents)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
