using System.Security.Cryptography;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Api.Auth;

public sealed class LoginDummyPasswordHash
{
    public LoginDummyPasswordHash(
        IOptions<PasswordHasherOptions> options)
    {
        var passwordHasher =
            new PasswordHasher<AppUser>(options);

        User =
            new AppUser
            {
                Id = Guid.NewGuid()
            };

        var secretBytes =
            RandomNumberGenerator.GetBytes(32);

        try
        {
            var secret =
                Convert.ToBase64String(secretBytes);

            Hash =
                passwordHasher.HashPassword(
                    User,
                    secret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                secretBytes);
        }
    }

    public AppUser User { get; }

    public string Hash { get; }
}
