namespace AmazonRepricer.Infrastructure.Identity;

public sealed class AuthRefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid FamilyId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? RevokedAtUtc { get; set; }

    public Guid? ReplacedByTokenId { get; set; }

    public string? RevocationReason { get; set; }

    public AppUser User { get; set; } = null!;

    public AuthRefreshToken? ReplacedByToken { get; set; }
}
