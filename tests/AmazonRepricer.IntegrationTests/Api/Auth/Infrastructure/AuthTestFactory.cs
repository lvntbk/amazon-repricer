using System.Security.Claims;
using AmazonRepricer.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal static class AuthTestFactory
{
    public static WebApplicationFactory<Program> Create()
    {
        return Create(_ => { });
    }

    public static WebApplicationFactory<Program> Create(
        Action<IWebHostBuilder> configure)
    {
        return CreateCore(
            configure,
            useProductionTokenStateValidation: false);
    }

    public static WebApplicationFactory<Program>
        CreateWithProductionTokenStateValidation()
    {
        return CreateCore(
            _ => { },
            useProductionTokenStateValidation: true);
    }

    private static WebApplicationFactory<Program> CreateCore(
        Action<IWebHostBuilder> configure,
        bool useProductionTokenStateValidation)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder =>
                {
                    builder.UseEnvironment("Testing");

                    configure(builder);

                    if (!useProductionTokenStateValidation)
                    {
                        builder.ConfigureServices(
                            services =>
                            {
                                services.RemoveAll<
                                    IAccessTokenStateValidator>();

                                services.AddSingleton<
                                    IAccessTokenStateValidator,
                                    AcceptAllAccessTokenStateValidator>();
                            });
                    }
                });
    }

    private sealed class AcceptAllAccessTokenStateValidator
        : IAccessTokenStateValidator
    {
        public Task<bool> IsValidAsync(
            ClaimsPrincipal principal,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
