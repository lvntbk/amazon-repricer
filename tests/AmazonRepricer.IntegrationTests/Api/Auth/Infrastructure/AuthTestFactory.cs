using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(
                builder =>
                {
                    builder.UseEnvironment("Testing");
                    configure(builder);
                });
    }
}
