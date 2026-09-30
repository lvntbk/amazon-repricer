using System.Security.Claims;
using AmazonRepricer.Api.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/auth/2fa")]
[EnableRateLimiting(AuthRateLimitPolicies.Login)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TwoFactorController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly AuthDbContext _db;

    public TwoFactorController(
        UserManager<AppUser> users,
        AuthDbContext db)
    {
        _users = users;
        _db = db;
    }

    [HttpPost("setup")]
    public async Task<IActionResult> Setup(SetupRequest request)
    {
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            return Unauthorized();
        }

        var ct = HttpContext.RequestAborted;
        await using var transaction =
            await AuthUserLock.AcquireAsync(_db, userId, ct);

        var user = await ValidateUserAsync(userId, request.Password);
        if (user is null)
        {
            await transaction.CommitAsync(ct);
            return Unauthorized();
        }

        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return Conflict(new { Error = "Two-factor authentication is already enabled." });
        }

        // Yeni kurulum, önceki tamamlanmamış kurulumu geçersiz kılar.
        var reset = await _users.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
        {
            return Problem(statusCode: 500, detail: "Authenticator setup failed.");
        }

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return Problem(statusCode: 500, detail: "Authenticator key is unavailable.");
        }

        var resetFailures = await _users.ResetAccessFailedCountAsync(user);
        if (!resetFailures.Succeeded)
        {
            return Problem(statusCode: 500, detail: "User security state could not be updated.");
        }

        await RevokeRefreshTokensAsync(user.Id, "TwoFactorSetup");
        await transaction.CommitAsync(ct);

        const string issuer = "AmazonRepricer";
        var label = Uri.EscapeDataString(
            $"{issuer}:{user.Email ?? user.Id.ToString()}");
        var uri =
            $"otpauth://totp/{label}?secret={Uri.EscapeDataString(key)}" +
            $"&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30&algorithm=SHA1";

        return Ok(new
        {
            SharedKey = key,
            AuthenticatorUri = uri,
            RequiresSignIn = true
        });
    }

    [HttpPost("enable")]
    public async Task<IActionResult> Enable(EnableRequest request)
    {
        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var userId))
        {
            return Unauthorized();
        }

        var ct = HttpContext.RequestAborted;
        await using var transaction =
            await AuthUserLock.AcquireAsync(_db, userId, ct);

        var user = await ValidateUserAsync(userId, request.Password);
        if (user is null)
        {
            await transaction.CommitAsync(ct);
            return Unauthorized();
        }

        if (await _users.GetTwoFactorEnabledAsync(user))
        {
            return Conflict(new { Error = "Two-factor authentication is already enabled." });
        }

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return BadRequest(new { Error = "Set up an authenticator first." });
        }

        var code = request.Code?.Trim();
        var valid =
            code is { Length: 6 } &&
            code.All(c => c >= '0' && c <= '9') &&
            await _users.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, code);

        if (!valid)
        {
            await _users.AccessFailedAsync(user);
            await transaction.CommitAsync(ct);
            return Unauthorized();
        }

        var enabled = await _users.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return Problem(statusCode: 500, detail: "Two-factor activation failed.");
        }

        var codes =
            (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))
            ?.ToArray();

        if (codes is null || codes.Length != 10)
        {
            return Problem(statusCode: 500, detail: "Recovery codes could not be generated.");
        }

        var resetFailures = await _users.ResetAccessFailedCountAsync(user);
        if (!resetFailures.Succeeded)
        {
            return Problem(statusCode: 500, detail: "User security state could not be updated.");
        }

        var stamp = await _users.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded)
        {
            return Problem(statusCode: 500, detail: "Existing sessions could not be invalidated.");
        }

        await RevokeRefreshTokensAsync(user.Id, "TwoFactorEnabled");
        await transaction.CommitAsync(ct);

        return Ok(new
        {
            TwoFactorEnabled = true,
            RecoveryCodes = codes,
            RequiresSignIn = true
        });
    }

    private async Task<AppUser?> ValidateUserAsync(
        Guid userId,
        string? password)
    {
        var user = await _users.FindByIdAsync(userId.ToString());

        // Yetkilendirme ile kilidin alınması arasında oturum iptal edilmiş olabilir.
        if (user is null ||
            !user.IsActive ||
            string.IsNullOrWhiteSpace(user.SecurityStamp) ||
            !string.Equals(
                user.SecurityStamp,
                User.FindFirstValue(AppJwtClaimTypes.SecurityStamp),
                StringComparison.Ordinal) ||
            await _users.IsLockedOutAsync(user) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        if (!await _users.CheckPasswordAsync(user, password))
        {
            await _users.AccessFailedAsync(user);
            return null;
        }

        return user;
    }

    private Task<int> RevokeRefreshTokensAsync(Guid userId, string reason)
    {
        var now = DateTime.UtcNow;

        return _db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        x => x.RevokedAtUtc,
                        x => x.CreatedAtUtc > now ? x.CreatedAtUtc : now)
                    .SetProperty(x => x.RevocationReason, reason),
                HttpContext.RequestAborted);
    }

    public sealed record SetupRequest(string Password);
    public sealed record EnableRequest(string Password, string Code);
}
