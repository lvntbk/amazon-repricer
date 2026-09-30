using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AmazonRepricer.Application.Auth;
using AmazonRepricer.Infrastructure.Identity;
using AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;
using AmazonRepricer.IntegrationTests.PostgreSql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AmazonRepricer.IntegrationTests.Api.Auth;

[Collection(PostgreSqlCollection.Name)]
public sealed class TwoFactorPostgreSqlTests
{
    private readonly PostgreSqlFixture _database;
    private const string Password = "Correct-Horse-2026!";

    public TwoFactorPostgreSqlTests(PostgreSqlFixture database)
    {
        _database = database;
    }

    [Theory]
    [InlineData("login")]
    [InlineData("recovery")]
    [InlineData("concurrent-recovery")]
    [InlineData("sessions")]
    [InlineData("lockout")]
    [InlineData("enrollment-admin")]
    [InlineData("enrollment-operator")]
    public async Task TwoFactor_EnforcesSecurityRules(string scenario)
    {
        using var environment = AuthTestEnvironmentScope.Capture(
            "ConnectionStrings__DefaultConnection",
            "Jwt__Issuer",
            "Jwt__Audience",
            "Jwt__SigningKey");

        environment.Set(
            "ConnectionStrings__DefaultConnection",
            _database.ConnectionString);
        environment.Set("Jwt__Issuer", "AmazonRepricer.IntegrationTests");
        environment.Set("Jwt__Audience", "AmazonRepricer.IntegrationTests");
        environment.Set(
            "Jwt__SigningKey",
            "integration-test-signing-key-32-bytes-minimum");

        using var factory =
            AuthTestFactory.CreateWithProductionTokenStateValidation();
        using var client = factory.CreateClient();

        var userId = Guid.NewGuid();
        var email = $"twofactor-{userId:N}@example.test";

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                await AuthTestData.CreateUserAsync(
                    scope.ServiceProvider,
                    email,
                    Password,
                    scenario == "enrollment-admin"
                        ? AppRoles.Admin
                        : AppRoles.Operator,
                    userId: userId);
            }

            // İlk oturum ve Authenticator kurulumu.
            var initial = await LoginAsync();
            Authenticate(initial.AccessToken);

            // Kurulum tamamlanmadan iş uçlarına erişim yasak.
            using var blocked = await client.GetAsync("/api/products");
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

            using var setupResponse = await client.PostAsJsonAsync(
                "/api/auth/2fa/setup",
                new { Password });

            Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
            Assert.True(setupResponse.Headers.CacheControl?.NoStore == true);

            var setup =
                await setupResponse.Content.ReadFromJsonAsync<SetupPayload>();
            Assert.NotNull(setup);
            Assert.False(string.IsNullOrWhiteSpace(setup.SharedKey));
            Assert.StartsWith("otpauth://totp/", setup.AuthenticatorUri);
            Assert.True(setup.RequiresSignIn);

            // Kurulum eski access ve refresh tokenlarını kapatmalı.
            using var staleAccess = await client.GetAsync("/api/products");
            Assert.Equal(HttpStatusCode.Unauthorized, staleAccess.StatusCode);

            using var staleRefresh = await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new { initial.RefreshToken });
            Assert.Equal(HttpStatusCode.Unauthorized, staleRefresh.StatusCode);

            // Kurulum sonrası yeniden giriş.
            var beforeEnable = await LoginAsync();
            Authenticate(beforeEnable.AccessToken);

            // Yanlış kod, 2FA'yı açmamalı.
            using var invalidEnable = await client.PostAsJsonAsync(
                "/api/auth/2fa/enable",
                new { Password, Code = "invalid" });
            Assert.Equal(HttpStatusCode.Unauthorized, invalidEnable.StatusCode);

            await using (var db = _database.CreateAuthDbContext())
            {
                var user = await db.Users.SingleAsync(x => x.Id == userId);
                Assert.False(user.TwoFactorEnabled);
            }

            // Kodu Identity sağlayıcısından değil, bağımsız TOTP hesabından üret.
            using var enableResponse = await client.PostAsJsonAsync(
                "/api/auth/2fa/enable",
                new { Password, Code = CreateTotp(setup.SharedKey) });

            Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
            Assert.True(enableResponse.Headers.CacheControl?.NoStore == true);

            var enabled =
                await enableResponse.Content.ReadFromJsonAsync<EnablePayload>();
            Assert.NotNull(enabled);
            Assert.True(enabled.TwoFactorEnabled);
            Assert.True(enabled.RequiresSignIn);
            Assert.Equal(10, enabled.RecoveryCodes.Length);
            Assert.Equal(10, enabled.RecoveryCodes.Distinct().Count());

            switch (scenario)
            {
                case "enrollment-admin":
                case "enrollment-operator":
                {
                    var tokens = await LoginAsync(
                        code: CreateTotp(setup.SharedKey));
                    Authenticate(tokens.AccessToken);

                    using var allowed = await client.GetAsync("/api/products");
                    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
                    break;
                }

                case "login":
                {
                    using var missing = await LoginResponseAsync();
                    Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

                    using var wrong = await LoginResponseAsync(code: "invalid");
                    Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

                    var tokens = await LoginAsync(code: CreateTotp(setup.SharedKey));
                    Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));
                    Assert.False(string.IsNullOrWhiteSpace(tokens.RefreshToken));
                    break;
                }

                case "recovery":
                {
                    var tokens = await LoginAsync(
                        recovery: enabled.RecoveryCodes[0]);
                    Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));

                    using var reused = await LoginResponseAsync(
                        recovery: enabled.RecoveryCodes[0]);
                    Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);

                    using var scope = factory.Services.CreateScope();
                    var users = scope.ServiceProvider
                        .GetRequiredService<UserManager<AppUser>>();
                    var user = await users.FindByIdAsync(userId.ToString());
                    Assert.NotNull(user);
                    Assert.Equal(
                        9,
                        await users.CountRecoveryCodesAsync(user));
                    break;
                }

                case "concurrent-recovery":
                {
                    var responses = await Task.WhenAll(
                        LoginResponseAsync(recovery: enabled.RecoveryCodes[0]),
                        LoginResponseAsync(recovery: enabled.RecoveryCodes[0]));

                    try
                    {
                        Assert.Single(
                            responses,
                            x => x.StatusCode == HttpStatusCode.OK);
                        Assert.Single(
                            responses,
                            x => x.StatusCode == HttpStatusCode.Unauthorized);
                    }
                    finally
                    {
                        foreach (var response in responses)
                            response.Dispose();
                    }

                    break;
                }

                case "sessions":
                {
                    // Enable öncesindeki JWT artık geçersiz.
                    using var oldAccess = await client.GetAsync("/api/products");
                    Assert.Equal(
                        HttpStatusCode.Unauthorized,
                        oldAccess.StatusCode);

                    using var oldRefresh = await client.PostAsJsonAsync(
                        "/api/auth/refresh",
                        new { beforeEnable.RefreshToken });
                    Assert.Equal(
                        HttpStatusCode.Unauthorized,
                        oldRefresh.StatusCode);

                    await using var db = _database.CreateAuthDbContext();
                    Assert.False(await db.RefreshTokens.AnyAsync(
                        x => x.UserId == userId && x.RevokedAtUtc == null));
                    break;
                }

                case "lockout":
                {
                    for (var i = 0; i < 4; i++)
                    {
                        using var wrong =
                            await LoginResponseAsync(code: "invalid");
                        Assert.Equal(
                            HttpStatusCode.Unauthorized,
                            wrong.StatusCode);
                    }

                    // Kilitliyken doğru kod da oturum açamamalı.
                    using var locked =
                        await LoginResponseAsync(code: CreateTotp(setup.SharedKey));
                    Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);

                    using var scope = factory.Services.CreateScope();
                    var users = scope.ServiceProvider
                        .GetRequiredService<UserManager<AppUser>>();
                    var user = await users.FindByIdAsync(userId.ToString());
                    Assert.NotNull(user);
                    Assert.True(await users.IsLockedOutAsync(user));
                    break;
                }
            }
        }
        finally
        {
            await using var cleanup = _database.CreateAuthDbContext();
            var user = await cleanup.Users.FindAsync(userId);
            if (user is not null)
            {
                cleanup.Users.Remove(user);
                await cleanup.SaveChangesAsync();
            }
        }

        void Authenticate(string token)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        Task<HttpResponseMessage> LoginResponseAsync(
            string? code = null,
            string? recovery = null)
        {
            return client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    Email = email,
                    Password,
                    TwoFactorCode = code,
                    RecoveryCode = recovery
                });
        }

        async Task<TokenPayload> LoginAsync(
            string? code = null,
            string? recovery = null)
        {
            using var response = await LoginResponseAsync(code, recovery);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var result = await response.Content.ReadFromJsonAsync<TokenPayload>();
            Assert.NotNull(result);
            return result;
        }
    }

    private static string CreateTotp(string base32)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;

        foreach (var character in base32.TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(character);
            if (value < 0)
                throw new InvalidOperationException("Invalid Base32 key.");

            buffer = (buffer << 5) | value;
            bits += 5;

            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(
            counter,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);

        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var number =
            BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4))
            & int.MaxValue;

        return (number % 1_000_000).ToString(
            "D6",
            CultureInfo.InvariantCulture);
    }

    private sealed record TokenPayload(string AccessToken, string RefreshToken);
    private sealed record SetupPayload(
        string SharedKey,
        string AuthenticatorUri,
        bool RequiresSignIn);
    private sealed record EnablePayload(
        bool TwoFactorEnabled,
        string[] RecoveryCodes,
        bool RequiresSignIn);
}
