using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AmazonRepricer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal sealed class EnrolledTestSession : IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly Guid? _ownedUserId;

    public HttpClient Client { get; }

    private EnrolledTestSession(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        Guid? ownedUserId)
    {
        _factory = factory;
        Client = client;
        _ownedUserId = ownedUserId;
    }

    public static async Task<EnrolledTestSession> CreateAsync(
        WebApplicationFactory<Program> factory,
        string role,
        Guid? existingUserId = null,
        string password = "Enrolled-Test-2026!")
    {
        var client = AuthTestClientFactory.Create(factory);
        var userId = existingUserId ?? Guid.NewGuid();
        var session = new EnrolledTestSession(
            factory, client, existingUserId is null ? userId : null);

        try
        {
            string email;

            using (var scope = factory.Services.CreateScope())
            {
                if (existingUserId is null)
                {
                    email = $"enrolled-{userId:N}@example.test";
                    await AuthTestData.CreateUserAsync(
                        scope.ServiceProvider,
                        email,
                        password,
                        role,
                        userId: userId);
                }
                else
                {
                    var users = scope.ServiceProvider
                        .GetRequiredService<UserManager<AppUser>>();
                    var user = await users.FindByIdAsync(userId.ToString());
                    Assert.NotNull(user);
                    Assert.True(await users.IsInRoleAsync(user, role));
                    email = user.Email!;
                }
            }

            var token = await SignInAsync(factory, client, email, password);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    // Bu yardımcı, iş kuralı testleri için önceden kurulmuş 2FA durumunu hazırlar.
    // Kurulum uçlarının kendisi TwoFactorPostgreSqlTests içinde test ediliyor.
    public static async Task<string> SignInAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string email,
        string password)
    {
        string recoveryCode;

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider
                .GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);

            EnsureSucceeded(await users.ResetAuthenticatorKeyAsync(user));
            EnsureSucceeded(await users.SetTwoFactorEnabledAsync(user, true));

            var codes =
                await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1);
            Assert.NotNull(codes);
            recoveryCode = Assert.Single(codes);
        }

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                Email = email,
                Password = password,
                RecoveryCode = recoveryCode
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>();
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.AccessToken));
        return payload.AccessToken;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();

        if (_ownedUserId is not Guid userId)
            return;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        await db.RefreshTokens
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync();

        await db.Users
            .Where(x => x.Id == userId)
            .ExecuteDeleteAsync();
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        Assert.True(
            result.Succeeded,
            string.Join("; ", result.Errors.Select(
                x => $"{x.Code}: {x.Description}")));
    }

    private sealed record TokenPayload(string AccessToken);
}
