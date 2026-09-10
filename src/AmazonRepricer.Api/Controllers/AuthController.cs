using AmazonRepricer.Api.Auth;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly JwtOptions _jwtOptions;
    private readonly IAccessTokenService _accessTokenService;
    private readonly ILoginTimingProtector _loginTimingProtector;
    private readonly AuthDbContext _authDbContext;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;

    public AuthController(
        UserManager<AppUser> userManager,
        IOptions<JwtOptions> jwtOptions,
        IAccessTokenService accessTokenService,
        ILoginTimingProtector loginTimingProtector,
        AuthDbContext authDbContext,
        IRefreshTokenGenerator refreshTokenGenerator)
    {
        _userManager = userManager;
        _jwtOptions = jwtOptions.Value;
        _accessTokenService = accessTokenService;
        _loginTimingProtector = loginTimingProtector;
        _authDbContext = authDbContext;
        _refreshTokenGenerator = refreshTokenGenerator;
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Login)]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByEmailAsync(
                request.Email.Trim());

        if (user is null)
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        if (!user.IsActive)
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            _loginTimingProtector.VerifyDummyPassword(
                request.Password);

            return Unauthorized();
        }

        var passwordIsValid =
            await _userManager.CheckPasswordAsync(
                user,
                request.Password);

        if (!passwordIsValid)
        {
            var accessFailedResult =
                await _userManager.AccessFailedAsync(user);

            if (!accessFailedResult.Succeeded)
            {
                return Unauthorized();
            }

            return Unauthorized();
        }

        var resetResult =
            await _userManager.ResetAccessFailedCountAsync(user);

        if (!resetResult.Succeeded)
        {
            return Unauthorized();
        }

        var roles =
            await _userManager.GetRolesAsync(user);

        var now =
            DateTimeOffset.UtcNow;

        var expiresAtUtc =
            now.Add(
                TimeSpan.FromMinutes(
                    _jwtOptions.AccessTokenLifetimeMinutes));

        var refreshTokenExpiresAtUtc =
            now.Add(
                TimeSpan.FromDays(
                    _jwtOptions.RefreshTokenLifetimeDays));

        var accessToken =
            _accessTokenService.CreateAccessToken(
                user,
                roles,
                now,
                expiresAtUtc);

        var generatedRefreshToken =
            _refreshTokenGenerator.Generate();

        var refreshToken =
            new AuthRefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FamilyId = Guid.NewGuid(),
                TokenHash =
                    generatedRefreshToken.TokenHash,
                CreatedAtUtc =
                    now.UtcDateTime,
                ExpiresAtUtc =
                    refreshTokenExpiresAtUtc.UtcDateTime
            };

        _authDbContext.RefreshTokens.Add(
            refreshToken);

        await _authDbContext.SaveChangesAsync(
            HttpContext.RequestAborted);

        return Ok(
            new LoginResponse(
                accessToken,
                "Bearer",
                expiresAtUtc,
                generatedRefreshToken.PlaintextToken,
                refreshTokenExpiresAtUtc));
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimitPolicies.Refresh)]
    [HttpPost("refresh")]
    public async Task<ActionResult<LoginResponse>> Refresh(
        RefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(
            request.RefreshToken))
        {
            return Unauthorized();
        }

        var cancellationToken =
            HttpContext.RequestAborted;

        var tokenHash =
            _refreshTokenGenerator.Hash(
                request.RefreshToken);

        var now =
            DateTimeOffset.UtcNow;

        var existingRefreshToken =
            await _authDbContext.RefreshTokens
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.TokenHash == tokenHash,
                    cancellationToken);

        if (existingRefreshToken is null)
        {
            return Unauthorized();
        }

        if (existingRefreshToken.ReplacedByTokenId is not null)
        {
            await RevokeRefreshTokenFamilyForReplayAsync(
                existingRefreshToken.UserId,
                existingRefreshToken.FamilyId,
                now.UtcDateTime,
                cancellationToken);

            return Unauthorized();
        }

        if (existingRefreshToken.RevokedAtUtc is not null ||
            existingRefreshToken.ExpiresAtUtc <=
                now.UtcDateTime)
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByIdAsync(
                existingRefreshToken.UserId.ToString());

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(user))
        {
            return Unauthorized();
        }

        var roles =
            await _userManager.GetRolesAsync(user);

        var generatedRefreshToken =
            _refreshTokenGenerator.Generate();

        var replacementTokenId =
            Guid.NewGuid();

        var refreshTokenExpiresAtUtc =
            now.Add(
                TimeSpan.FromDays(
                    _jwtOptions.RefreshTokenLifetimeDays));

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        _authDbContext.RefreshTokens.Add(
            new AuthRefreshToken
            {
                Id = replacementTokenId,
                UserId = user.Id,
                FamilyId =
                    existingRefreshToken.FamilyId,
                TokenHash =
                    generatedRefreshToken.TokenHash,
                CreatedAtUtc =
                    now.UtcDateTime,
                ExpiresAtUtc =
                    refreshTokenExpiresAtUtc.UtcDateTime
            });

        await _authDbContext.SaveChangesAsync(
            cancellationToken);

        var rotatedCount =
            await _authDbContext.RefreshTokens
                .Where(
                    x =>
                        x.Id ==
                            existingRefreshToken.Id &&
                        x.RevokedAtUtc == null &&
                        x.ReplacedByTokenId == null &&
                        x.ExpiresAtUtc >
                            now.UtcDateTime)
                .ExecuteUpdateAsync(
                    setters =>
                        setters
                            .SetProperty(
                                x => x.RevokedAtUtc,
                                now.UtcDateTime)
                            .SetProperty(
                                x => x.ReplacedByTokenId,
                                replacementTokenId)
                            .SetProperty(
                                x => x.RevocationReason,
                                "Rotated"),
                    cancellationToken);

        if (rotatedCount != 1)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            await transaction.DisposeAsync();

            await RevokeRefreshTokenFamilyForReplayAsync(
                existingRefreshToken.UserId,
                existingRefreshToken.FamilyId,
                now.UtcDateTime,
                cancellationToken);

            return Unauthorized();
        }

        await transaction.CommitAsync(
            cancellationToken);

        var accessTokenExpiresAtUtc =
            now.Add(
                TimeSpan.FromMinutes(
                    _jwtOptions.AccessTokenLifetimeMinutes));

        var accessToken =
            _accessTokenService.CreateAccessToken(
                user,
                roles,
                now,
                accessTokenExpiresAtUtc);

        return Ok(
            new LoginResponse(
                accessToken,
                "Bearer",
                accessTokenExpiresAtUtc,
                generatedRefreshToken.PlaintextToken,
                refreshTokenExpiresAtUtc));
    }

    private async Task RevokeRefreshTokenFamilyForReplayAsync(
        Guid userId,
        Guid familyId,
        DateTime revokedAtUtc,
        CancellationToken cancellationToken)
    {
        await _authDbContext.RefreshTokens
            .Where(
                x =>
                    x.UserId == userId &&
                    x.FamilyId == familyId &&
                    x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            x => x.RevokedAtUtc,
                            x =>
                                x.CreatedAtUtc > revokedAtUtc
                                    ? x.CreatedAtUtc
                                    : revokedAtUtc)
                        .SetProperty(
                            x => x.RevocationReason,
                            "ReplayDetected"),
                cancellationToken);
    }

    public sealed record LoginRequest(
        string Email,
        string Password);

    public sealed record RefreshRequest(
        string RefreshToken);

    public sealed record LoginResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAtUtc,
        string RefreshToken,
        DateTimeOffset RefreshTokenExpiresAtUtc);
}
