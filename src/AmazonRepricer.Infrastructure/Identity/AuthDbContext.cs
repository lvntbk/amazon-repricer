using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Infrastructure.Identity;

public sealed class AuthDbContext
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public AuthDbContext(
        DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuthRefreshToken> RefreshTokens =>
        Set<AuthRefreshToken>();

    protected override void OnModelCreating(
        ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AuthRefreshToken>(entity =>
        {
            entity.ToTable("AuthRefreshTokens");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.TokenHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.Property(x => x.ExpiresAtUtc)
                .IsRequired();

            entity.Property(x => x.RevocationReason)
                .HasMaxLength(256);

            entity.HasIndex(x => x.TokenHash)
                .IsUnique();

            entity.HasIndex(x => x.UserId);

            entity.HasIndex(x => x.FamilyId);

            entity.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.ReplacedByToken)
                .WithMany()
                .HasForeignKey(x => x.ReplacedByTokenId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(
                table =>
                {
                    table.HasCheckConstraint(
                        "CK_AuthRefreshTokens_Expiry",
                        "\"ExpiresAtUtc\" > \"CreatedAtUtc\"");

                    table.HasCheckConstraint(
                        "CK_AuthRefreshTokens_RevokedAt",
                        "\"RevokedAtUtc\" IS NULL OR \"RevokedAtUtc\" >= \"CreatedAtUtc\"");

                    table.HasCheckConstraint(
                        "CK_AuthRefreshTokens_NotSelfReplacement",
                        "\"ReplacedByTokenId\" IS NULL OR \"ReplacedByTokenId\" <> \"Id\"");

                    table.HasCheckConstraint(
                        "CK_AuthRefreshTokens_ReplacementRequiresRevocation",
                        "\"ReplacedByTokenId\" IS NULL OR \"RevokedAtUtc\" IS NOT NULL");
                });
        });

        builder.Entity<AppUser>(entity =>
        {
            entity.Property(x => x.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.Property(x => x.Email)
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(x => x.NormalizedEmail)
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(x => x.NormalizedEmail)
                .HasDatabaseName("EmailIndex")
                .IsUnique();
        });
    }
}
