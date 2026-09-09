using Microsoft.AspNetCore.Identity;

namespace AmazonRepricer.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
