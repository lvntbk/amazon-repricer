using System.Security.Cryptography;
using System.Text;
using AmazonRepricer.Application.Auth;

namespace AmazonRepricer.Infrastructure.Identity;

public sealed class RefreshTokenGenerator
    : IRefreshTokenGenerator
{
    private const int TokenSizeBytes = 32;

    public GeneratedRefreshToken Generate()
    {
        var tokenBytes =
            RandomNumberGenerator.GetBytes(
                TokenSizeBytes);

        try
        {
            var plaintextToken =
                Convert.ToHexString(
                    tokenBytes);

            var tokenHash =
                Hash(
                    plaintextToken);

            return new GeneratedRefreshToken(
                plaintextToken,
                tokenHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                tokenBytes);
        }
    }

    public string Hash(
        string plaintextToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            plaintextToken);

        var tokenBytes =
            Encoding.UTF8.GetBytes(
                plaintextToken);

        try
        {
            var hashBytes =
                SHA256.HashData(
                    tokenBytes);

            try
            {
                return Convert.ToHexString(
                    hashBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    hashBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                tokenBytes);
        }
    }
}
