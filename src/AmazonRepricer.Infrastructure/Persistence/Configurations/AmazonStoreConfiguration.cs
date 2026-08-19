using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class AmazonStoreConfiguration : IEntityTypeConfiguration<AmazonStore>
{
    public void Configure(EntityTypeBuilder<AmazonStore> builder)
    {
        builder.ToTable("amazon_stores");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.SellerId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.MarketplaceId)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => new { x.SellerId, x.MarketplaceId })
            .IsUnique();

        builder.HasMany(x => x.Products)
            .WithOne(x => x.AmazonStore)
            .HasForeignKey(x => x.AmazonStoreId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
