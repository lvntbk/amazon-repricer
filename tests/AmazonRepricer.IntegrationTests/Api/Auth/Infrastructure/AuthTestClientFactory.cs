using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AmazonRepricer.IntegrationTests.Api.Auth.Infrastructure;

internal static class AuthTestClientFactory
{
    public static HttpClient Create(
        WebApplicationFactory<Program> factory,
        string? role = null,
        Guid? userId = null)
    {
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

        if (role is not null)
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    AuthTestTokenFactory.Create(role, userId));
        }

        return client;
    }

    public static HttpClient CreateAuthenticated(
        WebApplicationFactory<Program> factory,
        string role,
        Guid? userId = null)
    {
        return Create(factory, role, userId);
    }
}
