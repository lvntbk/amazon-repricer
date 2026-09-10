using AmazonRepricer.Application.Auth;
using AmazonRepricer.Api.Auth;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IPricingEngine, PricingEngine>();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(
        builder.Configuration.GetSection(
            JwtOptions.SectionName))
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.Issuer),
        "JWT issuer is required.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.Audience),
        "JWT audience is required.")
    .Validate(
        options =>
            !string.IsNullOrWhiteSpace(options.SigningKey),
        "JWT signing key is required.")
    .Validate(
        options =>
            System.Text.Encoding.UTF8.GetByteCount(
                options.SigningKey) >= 32,
        "JWT signing key must be at least 32 bytes for HS256.")
    .Validate(
        options =>
            options.AccessTokenLifetimeMinutes is >= 1 and <= 60,
        "JWT access token lifetime must be between 1 and 60 minutes.")
    .Validate(
        options =>
            options.RefreshTokenLifetimeDays is >= 1 and <= 90,
        "JWT refresh token lifetime must be between 1 and 90 days.")
    .ValidateOnStart();

builder.Services.AddSingleton<
    IAccessTokenService,
    AccessTokenService>();

// Add services to the container.

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services
    .AddOptions<JwtBearerOptions>(
        JwtBearerDefaults.AuthenticationScheme)
    .Configure<
        Microsoft.Extensions.Options.IOptions<JwtOptions>>(
        (options, jwtOptionsAccessor) =>
        {
            var jwtOptions =
                jwtOptionsAccessor.Value;

            options.TokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer =
                        jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience =
                        jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(
                                jwtOptions.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
        });

builder.Services.AddSingleton<LoginDummyPasswordHash>();
builder.Services.AddScoped<
    ILoginTimingProtector,
    LoginTimingProtector>();

builder.Services.AddScoped<AuthBootstrapInitializer>();

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy =
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

    options.AddPolicy(
        AppAuthorizationPolicies.AdminOnly,
        policy =>
            policy.RequireRole(
                AppRoles.Admin));

    options.AddPolicy(
        AppAuthorizationPolicies.OperatorOrAdmin,
        policy =>
            policy.RequireRole(
                AppRoles.Admin,
                AppRoles.Operator));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.OnRejected =
        (context, _) =>
        {
            if (context.Lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var retryAfter))
            {
                var retryAfterSeconds =
                    Math.Max(
                        1,
                        (int)Math.Ceiling(
                            retryAfter.TotalSeconds));

                context.HttpContext.Response.Headers.RetryAfter =
                    retryAfterSeconds.ToString(
                        CultureInfo.InvariantCulture);
            }

            return ValueTask.CompletedTask;
        };

    options.AddPolicy(
        AuthRateLimitPolicies.Login,
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress
                        ?.ToString()
                    ?? "unknown",
                factory:
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder =
                            QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

    options.AddPolicy(
        AuthRateLimitPolicies.Refresh,
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey:
                    httpContext.Connection.RemoteIpAddress
                        ?.ToString()
                    ?? "unknown",
                factory:
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 20,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder =
                            QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

_ = app.Services
    .GetRequiredService<
        Microsoft.Extensions.Options.IOptions<JwtOptions>>()
    .Value;

await using (var scope =
    app.Services.CreateAsyncScope())
{
    var authBootstrapInitializer =
        scope.ServiceProvider
            .GetRequiredService<AuthBootstrapInitializer>();

    await authBootstrapInitializer
        .InitializeAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
