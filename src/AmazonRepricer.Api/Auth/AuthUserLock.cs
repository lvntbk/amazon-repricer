using AmazonRepricer.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AmazonRepricer.Api.Auth;

public static class AuthUserLock
{
    public static async Task<IDbContextTransaction> AcquireAsync(
        AuthDbContext dbContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        try
        {
            var lockName = $"auth-user:{userId:D}";

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockName}, 0))",
                cancellationToken);

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}
