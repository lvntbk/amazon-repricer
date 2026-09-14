using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[Authorize(Policy = AppAuthorizationPolicies.AdminOnly)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly AuthDbContext _authDbContext;

    public AdminUsersController(
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        AuthDbContext authDbContext)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _authDbContext = authDbContext;
    }

    [HttpPost]
    public async Task<ActionResult<CreateUserResponse>> CreateUser(
        CreateUserRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest();
        }

        var email =
            request.Email.Trim();

        var role =
            request.Role.Trim();

        if (role != AppRoles.Admin &&
            role != AppRoles.Operator)
        {
            return BadRequest(
                new
                {
                    Error = "Unsupported role."
                });
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            return Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                detail:
                    "Required application role is not configured.");
        }

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    HttpContext.RequestAborted);

        var user =
            new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = true
            };

        var createResult =
            await _userManager.CreateAsync(
                user,
                request.Password);

        if (!createResult.Succeeded)
        {
            return BadRequest(
                new
                {
                    Errors =
                        createResult.Errors.Select(
                            x =>
                                new
                                {
                                    x.Code,
                                    x.Description
                                })
                });
        }

        var roleResult =
            await _userManager.AddToRoleAsync(
                user,
                role);

        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync(
                HttpContext.RequestAborted);

            return Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                detail:
                    "User role assignment failed.");
        }

        await transaction.CommitAsync(
            HttpContext.RequestAborted);

        return Created(
            $"/api/admin/users/{user.Id}",
            new CreateUserResponse(
                user.Id,
                user.Email!,
                role,
                user.IsActive));
    }

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUser(
        Guid userId)
    {
        const long adminLifecycleLockKey =
            0x415554484C494645;

        var cancellationToken =
            HttpContext.RequestAborted;

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        await _authDbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({adminLifecycleLockKey})",
                cancellationToken);

        var user =
            await _userManager.FindByIdAsync(
                userId.ToString());

        if (user is null)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return NotFound();
        }

        if (user.IsActive)
        {
            var adminRole =
                await _roleManager.FindByNameAsync(
                    AppRoles.Admin);

            if (adminRole is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    detail:
                        "Required Admin role is not configured.");
            }

            var userIsAdmin =
                await _authDbContext.UserRoles
                    .AnyAsync(
                        x =>
                            x.UserId == user.Id &&
                            x.RoleId == adminRole.Id,
                        cancellationToken);

            if (userIsAdmin)
            {
                var activeAdminCount =
                    await (
                        from userRole in _authDbContext.UserRoles
                        join candidate in _authDbContext.Users
                            on userRole.UserId equals candidate.Id
                        where
                            userRole.RoleId == adminRole.Id &&
                            candidate.IsActive
                        select candidate.Id
                    )
                    .CountAsync(
                        cancellationToken);

                if (activeAdminCount <= 1)
                {
                    await transaction.RollbackAsync(
                        cancellationToken);

                    return Conflict(
                        new
                        {
                            Error =
                                "The last active Admin cannot be deactivated."
                        });
                }
            }

            user.IsActive = false;

            var updateResult =
                await _userManager.UpdateAsync(
                    user);

            if (!updateResult.Succeeded)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    detail:
                        "User deactivation failed.");
            }

            var securityStampResult =
                await _userManager
                    .UpdateSecurityStampAsync(
                        user);

            if (!securityStampResult.Succeeded)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    detail:
                        "User security state could not be updated.");
            }
        }

        var now =
            DateTime.UtcNow;

        await _authDbContext.RefreshTokens
            .Where(
                x =>
                    x.UserId == user.Id &&
                    x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            x => x.RevokedAtUtc,
                            x =>
                                x.CreatedAtUtc > now
                                    ? x.CreatedAtUtc
                                    : now)
                        .SetProperty(
                            x => x.RevocationReason,
                            "UserDeactivated"),
                cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return NoContent();
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<IActionResult> ChangeUserRole(
        Guid userId,
        ChangeUserRoleRequest request)
    {
        const long adminLifecycleLockKey =
            0x415554484C494645;

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest();
        }

        var requestedRole =
            request.Role.Trim();

        if (requestedRole != AppRoles.Admin &&
            requestedRole != AppRoles.Operator)
        {
            return BadRequest(
                new
                {
                    Error = "Unsupported role."
                });
        }

        var cancellationToken =
            HttpContext.RequestAborted;

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        await _authDbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({adminLifecycleLockKey})",
                cancellationToken);

        var user =
            await _userManager.FindByIdAsync(
                userId.ToString());

        if (user is null)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return NotFound();
        }

        var targetRole =
            await _roleManager.FindByNameAsync(
                requestedRole);

        if (targetRole is null)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                detail:
                    "Required application role is not configured.");
        }

        var currentRoles =
            await _userManager.GetRolesAsync(
                user);

        if (currentRoles.Count == 1 &&
            currentRoles[0] == requestedRole)
        {
            await transaction.CommitAsync(
                cancellationToken);

            return NoContent();
        }

        var currentlyAdmin =
            currentRoles.Contains(
                AppRoles.Admin,
                StringComparer.Ordinal);

        if (currentlyAdmin &&
            requestedRole != AppRoles.Admin &&
            user.IsActive)
        {
            var adminRole =
                await _roleManager.FindByNameAsync(
                    AppRoles.Admin);

            if (adminRole is null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    detail:
                        "Required Admin role is not configured.");
            }

            var activeAdminCount =
                await (
                    from userRole in _authDbContext.UserRoles
                    join candidate in _authDbContext.Users
                        on userRole.UserId equals candidate.Id
                    where
                        userRole.RoleId == adminRole.Id &&
                        candidate.IsActive
                    select candidate.Id
                )
                .Distinct()
                .CountAsync(
                    cancellationToken);

            if (activeAdminCount <= 1)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Conflict(
                    new
                    {
                        Error =
                            "The last active Admin cannot lose the Admin role."
                    });
            }
        }

        if (currentRoles.Count > 0)
        {
            var removeResult =
                await _userManager.RemoveFromRolesAsync(
                    user,
                    currentRoles);

            if (!removeResult.Succeeded)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    detail:
                        "Existing user roles could not be removed.");
            }
        }

        var addResult =
            await _userManager.AddToRoleAsync(
                user,
                requestedRole);

        if (!addResult.Succeeded)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                detail:
                    "User role assignment failed.");
        }

        var securityStampResult =
            await _userManager
                .UpdateSecurityStampAsync(
                    user);

        if (!securityStampResult.Succeeded)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            return Problem(
                statusCode:
                    StatusCodes.Status500InternalServerError,
                detail:
                    "User security state could not be updated.");
        }

        var now =
            DateTime.UtcNow;

        await _authDbContext.RefreshTokens
            .Where(
                x =>
                    x.UserId == user.Id &&
                    x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            x => x.RevokedAtUtc,
                            x =>
                                x.CreatedAtUtc > now
                                    ? x.CreatedAtUtc
                                    : now)
                        .SetProperty(
                            x => x.RevocationReason,
                            "RoleChanged"),
                cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return NoContent();
    }

    public sealed record CreateUserRequest(
        string Email,
        string Password,
        string Role);

    public sealed record CreateUserResponse(
        Guid Id,
        string Email,
        string Role,
        bool IsActive);

    public sealed record ChangeUserRoleRequest(
        string Role);
}
