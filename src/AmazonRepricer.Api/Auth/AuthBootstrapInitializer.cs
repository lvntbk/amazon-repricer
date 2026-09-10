using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AmazonRepricer.Api.Auth;

public sealed class AuthBootstrapInitializer
{
    private const long BootstrapLockKey =
        0x41555448424F4F54;

    private readonly AuthDbContext _authDbContext;
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly IConfiguration _configuration;

    public AuthBootstrapInitializer(
        AuthDbContext authDbContext,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IConfiguration configuration)
    {
        _authDbContext = authDbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _configuration = configuration;
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var options =
            _configuration
                .GetSection(AuthBootstrapOptions.SectionName)
                .Get<AuthBootstrapOptions>()
            ?? new AuthBootstrapOptions();

        if (options.Enabled &&
            (string.IsNullOrWhiteSpace(options.AdminEmail) ||
             string.IsNullOrWhiteSpace(options.AdminPassword)))
        {
            throw new InvalidOperationException(
                "Auth bootstrap is enabled but AdminEmail or AdminPassword is missing.");
        }

        await using var transaction =
            await _authDbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        await _authDbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({BootstrapLockKey})",
                cancellationToken);

        var adminRole =
            await EnsureRoleAsync(
                AppRoles.Admin,
                cancellationToken);

        await EnsureRoleAsync(
            AppRoles.Operator,
            cancellationToken);

        if (!options.Enabled)
        {
            await transaction.CommitAsync(
                cancellationToken);

            return;
        }

        var adminExists =
            await _authDbContext.UserRoles
                .AnyAsync(
                    x => x.RoleId == adminRole.Id,
                    cancellationToken);

        if (adminExists)
        {
            var activeAdminExists =
                await (
                    from userRole in _authDbContext.UserRoles
                    join candidate in _authDbContext.Users
                        on userRole.UserId equals candidate.Id
                    where
                        userRole.RoleId == adminRole.Id &&
                        candidate.IsActive
                    select candidate.Id
                )
                .AnyAsync(
                    cancellationToken);

            if (!activeAdminExists)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                throw new InvalidOperationException(
                    "Auth bootstrap cannot continue because no active Admin exists.");
            }

            await transaction.CommitAsync(
                cancellationToken);

            return;
        }

        var email =
            options.AdminEmail!.Trim();

        var existingUser =
            await _userManager.FindByEmailAsync(
                email);

        if (existingUser is not null)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                "Auth bootstrap AdminEmail already belongs to an existing non-Admin user.");
        }

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
                options.AdminPassword!);

        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                $"Initial Admin creation failed: {FormatErrors(createResult)}");
        }

        var addRoleResult =
            await _userManager.AddToRoleAsync(
                user,
                AppRoles.Admin);

        if (!addRoleResult.Succeeded)
        {
            await transaction.RollbackAsync(
                cancellationToken);

            throw new InvalidOperationException(
                $"Initial Admin role assignment failed: {FormatErrors(addRoleResult)}");
        }

        await transaction.CommitAsync(
            cancellationToken);
    }

    private async Task<IdentityRole<Guid>> EnsureRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        var existingRole =
            await _authDbContext.Roles
                .SingleOrDefaultAsync(
                    x => x.NormalizedName ==
                         roleName.ToUpperInvariant(),
                    cancellationToken);

        if (existingRole is not null)
        {
            return existingRole;
        }

        var role =
            new IdentityRole<Guid>(
                roleName);

        var createResult =
            await _roleManager.CreateAsync(
                role);

        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Required role '{roleName}' could not be created: {FormatErrors(createResult)}");
        }

        return role;
    }

    private static string FormatErrors(
        IdentityResult result)
    {
        return string.Join(
            "; ",
            result.Errors.Select(
                x => $"{x.Code}: {x.Description}"));
    }
}
