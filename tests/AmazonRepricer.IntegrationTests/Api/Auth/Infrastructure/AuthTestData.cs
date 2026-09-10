using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal static class AuthTestData
{
    public static async Task EnsureRoleAsync(
        IServiceProvider services,
        string role)
    {
        var roleManager =
            services.GetRequiredService<
                RoleManager<IdentityRole<Guid>>>();

        if (await roleManager.RoleExistsAsync(role))
        {
            return;
        }

        var result =
            await roleManager.CreateAsync(
                new IdentityRole<Guid>(role));

        EnsureSucceeded(
            result,
            $"Creating role '{role}'");
    }

    public static async Task EnsureRolesAsync(
        IServiceProvider services,
        params string[] roles)
    {
        foreach (var role in roles)
        {
            await EnsureRoleAsync(
                services,
                role);
        }
    }

    public static async Task<AppUser> CreateUserAsync(
        IServiceProvider services,
        string email,
        string password,
        string role,
        bool isActive = true,
        Guid? userId = null)
    {
        await EnsureRoleAsync(
            services,
            role);

        var userManager =
            services.GetRequiredService<
                UserManager<AppUser>>();

        var user =
            new AppUser
            {
                Id =
                    userId ??
                    Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                IsActive = isActive
            };

        var createResult =
            await userManager.CreateAsync(
                user,
                password);

        EnsureSucceeded(
            createResult,
            $"Creating test user '{email}'");

        var roleResult =
            await userManager.AddToRoleAsync(
                user,
                role);

        EnsureSucceeded(
            roleResult,
            $"Assigning role '{role}' to test user '{email}'");

        return user;
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors =
            string.Join(
                "; ",
                result.Errors.Select(
                    x =>
                        $"{x.Code}: {x.Description}"));

        throw new InvalidOperationException(
            $"{operation} failed: {errors}");
    }
}
