using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace AmazonRepricer.Api.Auth;

public sealed class LoginTimingProtector
    : ILoginTimingProtector
{
    private readonly IPasswordHasher<AppUser> _passwordHasher;
    private readonly LoginDummyPasswordHash _dummyPasswordHash;

    public LoginTimingProtector(
        IPasswordHasher<AppUser> passwordHasher,
        LoginDummyPasswordHash dummyPasswordHash)
    {
        _passwordHasher = passwordHasher;
        _dummyPasswordHash = dummyPasswordHash;
    }

    public void VerifyDummyPassword(
        string providedPassword)
    {
        _ = _passwordHasher.VerifyHashedPassword(
            _dummyPasswordHash.User,
            _dummyPasswordHash.Hash,
            providedPassword);
    }
}
