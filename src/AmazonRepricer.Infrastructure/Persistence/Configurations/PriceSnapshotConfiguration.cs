using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class PriceSnapshotConfiguration : IEntityTypeConfiguration<PriceSnapshot>
{
    public void Configure(EntityTypeBuilder<PriceSnapshot> builder)
    {
        builder.ToTable("price_snapshots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OurPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.FeaturedOfferPrice)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.ProductId, x.CapturedAtUtc });

        builder.HasOne(x => x.Product)
            .WithMany(x => x.PriceSnapshots)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
