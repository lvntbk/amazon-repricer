using AmazonRepricer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace AmazonRepricer.IntegrationTests.PostgreSql;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("amazon_repricing_integration_tests")
            .WithUsername("integration_test")
            .WithPassword("integration_test_password")
            .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public RepricerDbContext CreateDbContext(
        params IInterceptor[] interceptors)
    {
        var optionsBuilder =
            new DbContextOptionsBuilder<RepricerDbContext>()
                .UseNpgsql(_container.GetConnectionString());

        if (interceptors.Length > 0)
        {
            optionsBuilder.AddInterceptors(interceptors);
        }

        return new RepricerDbContext(optionsBuilder.Options);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlCollection
    : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration tests";
}
