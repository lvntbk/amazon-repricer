using AmazonRepricer.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AmazonRepricer.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sku)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Asin)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Cost)
            .HasPrecision(18, 2);

        builder.Property(x => x.CurrentPrice)
            .HasPrecision(18, 2);

        builder.HasIndex(x => new { x.AmazonStoreId, x.Sku })
            .IsUnique();

        builder.HasIndex(x => x.Asin);

        builder.HasOne(x => x.PricingRule)
            .WithOne(x => x.Product)
            .HasForeignKey<PricingRule>(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
