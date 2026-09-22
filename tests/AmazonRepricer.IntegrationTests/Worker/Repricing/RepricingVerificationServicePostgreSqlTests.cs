using AmazonRepricer.Application.Amazon;
using AmazonRepricer.Domain.Entities;
using AmazonRepricer.Domain.Enums;
using AmazonRepricer.Infrastructure.Amazon;
using AmazonRepricer.Infrastructure.Persistence;
using AmazonRepricer.IntegrationTests.PostgreSql;
using AmazonRepricer.Worker;
using AmazonRepricer.Worker.Repricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmazonRepricer.IntegrationTests.Worker.Repricing;

[Collection(PostgreSqlCollection.Name)]
public sealed class RepricingVerificationServicePostgreSqlTests
{
    private readonly PostgreSqlFixture _database;

    public RepricingVerificationServicePostgreSqlTests(
        PostgreSqlFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task MatchingPrice_CompletesEventAndUpdatesProduct()
    {
        var scenario = await SeedAsync();
        var reader = new StubReader(
            () => Task.FromResult(Observation(scenario)));

        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, scenario);

        Assert.Equal(1, await service.VerifyAsync(100));
        await AssertStateAsync(scenario, RepricingStatus.Applied, 99m);

        Assert.Equal(0, await service.VerifyAsync(100));
        Assert.Equal(1, reader.CallCount);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("currency")]
    [InlineData("missing-offer")]
    [InlineData("error")]
    public async Task InsufficientEvidence_KeepsEventWaiting(string reason)
    {
        var scenario = await SeedAsync();
        var observation = Observation(scenario);

        observation = reason switch
        {
            "price" => observation with
            {
                Offers = new[]
                {
                    observation.Offers[0] with { Price = 100m }
                }
            },
            "currency" => observation with
            {
                Offers = new[]
                {
                    observation.Offers[0] with { CurrencyCode = "USD" }
                }
            },
            "missing-offer" => observation with
            {
                Offers = Array.Empty<AmazonListingOffer>()
            },
            "error" => observation with
            {
                Issues = new[]
                {
                    new AmazonListingIssue(
                        "PRICE_ERROR", "ERROR", "Price rejected.",
                        new[] { "purchasable_offer" })
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(reason))
        };

        var reader = new StubReader(() => Task.FromResult(observation));
        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0,
            await CreateService(provider, scenario).VerifyAsync(100));

        Assert.Equal(1, reader.CallCount);
        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task ReadFailure_KeepsEventWaiting()
    {
        var scenario = await SeedAsync();
        var reader = new StubReader(
            () => Task.FromException<AmazonListingObservation>(
                new HttpRequestException("Simulated read failure.")));

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0,
            await CreateService(provider, scenario).VerifyAsync(100));

        Assert.Equal(1, reader.CallCount);
        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task ProductChangedDuringRead_DoesNotOverwriteNewPrice()
    {
        var scenario = await SeedAsync();

        var reader = new StubReader(async () =>
        {
            await using var context = _database.CreateDbContext();

            var product = await context.Products
                .SingleAsync(x => x.Id == scenario.ProductId);

            product.CurrentPrice = 105m;
            await context.SaveChangesAsync();

            return Observation(scenario);
        });

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0,
            await CreateService(provider, scenario).VerifyAsync(100));

        Assert.Equal(1, reader.CallCount);
        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 105m);
    }

    [Fact]
    public async Task ConcurrentVerification_UsesOneLeaseAndOneAmazonRead()
    {
        var scenario = await SeedAsync();

        var readStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRead = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var reader = new StubReader(async () =>
        {
            readStarted.TrySetResult(true);
            await releaseRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return Observation(scenario);
        });

        await using var provider = CreateProvider(reader);

        var first = CreateService(provider, scenario).VerifyAsync(100);

        try
        {
            await readStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var second = await CreateService(provider, scenario).VerifyAsync(100);

            Assert.Equal(0, second);
            Assert.Equal(1, reader.CallCount);
        }
        finally
        {
            releaseRead.TrySetResult(true);
        }

        Assert.Equal(1, await first);
        await AssertStateAsync(scenario, RepricingStatus.Applied, 99m);
    }

    [Fact]
    public async Task FailureAfterSave_RollsBackBothEventAndProduct()
    {
        var scenario = await SeedAsync();
        var interceptor = new FailAfterAppliedSaveInterceptor();

        var reader = new StubReader(
            () => Task.FromResult(Observation(scenario)));

        await using var provider = CreateProvider(reader, interceptor);

        Assert.Equal(
            0,
            await CreateService(provider, scenario).VerifyAsync(100));

        Assert.True(interceptor.WasTriggered);
        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task MockMode_DoesNotReadAmazonOrCompleteEvent()
    {
        var scenario = await SeedAsync();
        var reader = new StubReader(
            () => Task.FromResult(Observation(scenario)));

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0,
            await CreateService(provider, scenario, useMock: true)
                .VerifyAsync(100));

        Assert.Equal(0, reader.CallCount);
        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(5, 900)]
    public async Task RetryDelay_IsPersistedAndRespected(
        int previousAttempts,
        int expectedDelaySeconds)
    {
        var scenario = await SeedAsync();

        await using (var db = _database.CreateDbContext())
        {
            var item = await db.RepricingEvents
                .SingleAsync(x => x.Id == scenario.EventId);
            item.VerificationAttemptCount = previousAttempts;
            await db.SaveChangesAsync();
        }

        var reader = new StubReader(() => Task.FromResult(
            Observation(scenario) with
            {
                Offers = Array.Empty<AmazonListingOffer>()
            }));

        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, scenario);
        var before = DateTime.UtcNow;

        Assert.Equal(0, await service.VerifyAsync(100));

        var after = DateTime.UtcNow;

        await using var verification = _database.CreateDbContext();
        var persisted = await verification.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        Assert.Equal(previousAttempts + 1, persisted.VerificationAttemptCount);
        Assert.NotNull(persisted.LastVerificationAttemptAtUtc);
        Assert.NotNull(persisted.NextVerificationAttemptAtUtc);
        Assert.InRange(
            persisted.NextVerificationAttemptAtUtc!.Value,
            before.AddSeconds(expectedDelaySeconds).AddMilliseconds(-1),
            after.AddSeconds(expectedDelaySeconds).AddMilliseconds(1));
        Assert.False(persisted.VerificationReviewRequired);
        Assert.Null(persisted.VerificationLeaseId);
        Assert.Null(persisted.VerificationLeaseExpiresAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(persisted.LastVerificationReason));

        Assert.Equal(0, await service.VerifyAsync(100));
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task AttemptLimit_RequiresReviewWithoutApplyingPrice()
    {
        var scenario = await SeedAsync();

        await using (var db = _database.CreateDbContext())
        {
            var item = await db.RepricingEvents
                .SingleAsync(x => x.Id == scenario.EventId);
            item.VerificationAttemptCount = 9;
            await db.SaveChangesAsync();
        }

        var reader = new StubReader(() => Task.FromResult(
            Observation(scenario) with
            {
                Offers = Array.Empty<AmazonListingOffer>()
            }));

        await using var provider = CreateProvider(reader);
        var service = CreateService(provider, scenario);

        Assert.Equal(0, await service.VerifyAsync(100));
        Assert.Equal(0, await service.VerifyAsync(100));
        Assert.Equal(1, reader.CallCount);

        await using var verification = _database.CreateDbContext();
        var persisted = await verification.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        Assert.Equal(10, persisted.VerificationAttemptCount);
        Assert.True(persisted.VerificationReviewRequired);
        Assert.Null(persisted.NextVerificationAttemptAtUtc);
        Assert.Null(persisted.VerificationLeaseId);
        Assert.Null(persisted.VerificationLeaseExpiresAtUtc);

        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task ExpiredLease_AllowsRecoveryAndVerification()
    {
        var scenario = await SeedAsync();

        await using (var db = _database.CreateDbContext())
        {
            var item = await db.RepricingEvents
                .SingleAsync(x => x.Id == scenario.EventId);

            item.VerificationAttemptCount = 1;
            item.VerificationLeaseId = Guid.NewGuid();
            item.VerificationLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            item.NextVerificationAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var reader = new StubReader(
            () => Task.FromResult(Observation(scenario)));

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            1, await CreateService(provider, scenario).VerifyAsync(100));
        Assert.Equal(1, reader.CallCount);

        await using var verification = _database.CreateDbContext();
        var persisted = await verification.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        Assert.Equal(2, persisted.VerificationAttemptCount);
        Assert.Null(persisted.VerificationLeaseId);
        Assert.Null(persisted.VerificationLeaseExpiresAtUtc);
        Assert.Null(persisted.NextVerificationAttemptAtUtc);

        await AssertStateAsync(scenario, RepricingStatus.Applied, 99m);
    }

    [Fact]
    public async Task ExpiredFinalAttempt_RequiresReviewWithoutAnotherRead()
    {
        var scenario = await SeedAsync();

        await using (var db = _database.CreateDbContext())
        {
            var item = await db.RepricingEvents
                .SingleAsync(x => x.Id == scenario.EventId);

            item.VerificationAttemptCount = 10;
            item.VerificationLeaseId = Guid.NewGuid();
            item.VerificationLeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            item.NextVerificationAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var reader = new StubReader(
            () => Task.FromResult(Observation(scenario)));

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0, await CreateService(provider, scenario).VerifyAsync(100));
        Assert.Equal(0, reader.CallCount);

        await using var verification = _database.CreateDbContext();
        var persisted = await verification.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        Assert.True(persisted.VerificationReviewRequired);
        Assert.Equal(10, persisted.VerificationAttemptCount);
        Assert.Null(persisted.VerificationLeaseId);
        Assert.Null(persisted.VerificationLeaseExpiresAtUtc);
        Assert.Null(persisted.NextVerificationAttemptAtUtc);

        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task ReplacedLease_OldOwnerCannotCompleteOrReschedule()
    {
        var scenario = await SeedAsync();
        var replacementLease = Guid.NewGuid();
        var replacementExpiry = DateTime.UtcNow.AddMinutes(5);

        var reader = new StubReader(async () =>
        {
            await using var db = _database.CreateDbContext();
            var item = await db.RepricingEvents
                .SingleAsync(x => x.Id == scenario.EventId);

            // Simulate a later owner taking over after the original lease.
            item.VerificationLeaseId = replacementLease;
            item.VerificationLeaseExpiresAtUtc = replacementExpiry;
            item.NextVerificationAttemptAtUtc = replacementExpiry;
            item.LastVerificationReason = "Owned by replacement worker.";
            await db.SaveChangesAsync();

            return Observation(scenario);
        });

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            0, await CreateService(provider, scenario).VerifyAsync(100));

        await using var verification = _database.CreateDbContext();
        var persisted = await verification.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        Assert.Equal(replacementLease, persisted.VerificationLeaseId);
        Assert.NotNull(persisted.VerificationLeaseExpiresAtUtc);
        Assert.True(persisted.VerificationLeaseExpiresAtUtc > DateTime.UtcNow);
        Assert.Equal(
            persisted.VerificationLeaseExpiresAtUtc,
            persisted.NextVerificationAttemptAtUtc);
        Assert.Equal(
            "Owned by replacement worker.",
            persisted.LastVerificationReason);

        await AssertStateAsync(
            scenario, RepricingStatus.AwaitingVerification, 100m);
    }

    [Fact]
    public async Task DelayedOlderEvent_DoesNotBlockDueEvent_WithBatchSizeOne()
    {
        var older = await SeedAsync();
        var newer = await SeedAsync();

        await using (var db = _database.CreateDbContext())
        {
            var olderProduct = await db.Products
                .SingleAsync(x => x.Id == older.ProductId);
            var newerProduct = await db.Products
                .SingleAsync(x => x.Id == newer.ProductId);
            var olderEvent = await db.RepricingEvents
                .SingleAsync(x => x.Id == older.EventId);

            // Both products must belong to the configured seller.
            newerProduct.AmazonStoreId = olderProduct.AmazonStoreId;
            olderEvent.NextVerificationAttemptAtUtc =
                DateTime.UtcNow.AddMinutes(10);

            await db.SaveChangesAsync();
        }

        newer = newer with { SellerId = older.SellerId };

        var reader = new StubReader(
            () => Task.FromResult(Observation(newer)));

        await using var provider = CreateProvider(reader);

        Assert.Equal(
            1, await CreateService(provider, older).VerifyAsync(1));
        Assert.Equal(1, reader.CallCount);

        await AssertStateAsync(
            older, RepricingStatus.AwaitingVerification, 100m);
        await AssertStateAsync(
            newer, RepricingStatus.Applied, 99m);
    }

    private ServiceProvider CreateProvider(
        IAmazonListingReader reader,
        IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();

        services.AddScoped<RepricerDbContext>(_ =>
            interceptor is null
                ? _database.CreateDbContext()
                : _database.CreateDbContext(interceptor));

        services.AddSingleton<IAmazonListingReader>(reader);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
    }

    private static RepricingVerificationService CreateService(
        IServiceProvider provider,
        Scenario scenario,
        bool useMock = false)
    {
        return new RepricingVerificationService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RepricingVerificationService>.Instance,
            Options.Create(new AmazonSpApiOptions
            {
                UseMock = useMock,
                SellerId = scenario.SellerId,
                MarketplaceId = "A33AVAJ2PDY3EV"
            }),
            Options.Create(new WorkerOptions()));
    }

    private async Task<Scenario> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");

        var store = new AmazonStore
        {
            Name = $"Verification store {suffix}",
            SellerId = $"SELLER-{suffix}",
            MarketplaceId = "A33AVAJ2PDY3EV",
            IsActive = true,
            AutomaticRepricingEnabled = true
        };

        var product = new Product
        {
            AmazonStoreId = store.Id,
            AmazonStore = store,
            Sku = $"VERIFY-{suffix}",
            Asin = "B0VERIFY01",
            Title = "Verification test product",
            ProductType = "PRODUCT",
            CurrencyCode = "TRY",
            CurrentPrice = 100m,
            IsRepricingEnabled = true
        };

        var item = new RepricingEvent
        {
            ProductId = product.Id,
            Product = product,
            OldPrice = 100m,
            ProposedPrice = 99m,
            Reason = "Verification integration test."
        };

        item.Approve("Approved for test.");
        item.BeginApplication();
        item.RecordAmazonSubmission(
            true, $"submission-{suffix}", Array.Empty<string>());
        item.MarkAwaitingVerification();

        await using var context = _database.CreateDbContext();
        context.RepricingEvents.Add(item);
        await context.SaveChangesAsync();

        return new Scenario(item.Id, product.Id, store.SellerId, product.Sku);
    }

    private static AmazonListingObservation Observation(Scenario scenario)
    {
        var now = DateTimeOffset.UtcNow;

        return new AmazonListingObservation(
            scenario.SellerId,
            scenario.Sku,
            "A33AVAJ2PDY3EV",
            now,
            now,
            new[]
            {
                new AmazonListingOffer(
                    "A33AVAJ2PDY3EV", "B2C", 99m, "TRY")
            },
            Array.Empty<AmazonListingIssue>());
    }

    private async Task AssertStateAsync(
        Scenario scenario,
        RepricingStatus expectedStatus,
        decimal expectedCurrentPrice)
    {
        await using var context = _database.CreateDbContext();

        var item = await context.RepricingEvents
            .AsNoTracking()
            .SingleAsync(x => x.Id == scenario.EventId);

        var price = await context.Products
            .AsNoTracking()
            .Where(x => x.Id == scenario.ProductId)
            .Select(x => x.CurrentPrice)
            .SingleAsync();

        Assert.Equal(expectedStatus, item.Status);
        Assert.Equal(expectedCurrentPrice, price);
        Assert.True(item.AmazonSubmissionAccepted == true);
        Assert.NotNull(item.SubmittedAtUtc);

        if (expectedStatus == RepricingStatus.Applied)
        {
            Assert.True(item.WasApplied);
            Assert.Equal(99m, item.AppliedPrice);
            Assert.NotNull(item.ProcessedAtUtc);
            Assert.NotNull(item.ReconciledAtUtc);
        }
        else
        {
            Assert.False(item.WasApplied);
            Assert.Null(item.AppliedPrice);
            Assert.Null(item.ProcessedAtUtc);
            Assert.Null(item.ReconciledAtUtc);
        }
    }

    private sealed record Scenario(
        Guid EventId,
        Guid ProductId,
        string SellerId,
        string Sku);

    private sealed class StubReader(
        Func<Task<AmazonListingObservation>> read) : IAmazonListingReader
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public Task<AmazonListingObservation> GetListingAsync(
            string sellerId,
            string sku,
            string marketplaceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return read();
        }
    }

    private sealed class FailAfterAppliedSaveInterceptor : SaveChangesInterceptor
    {
        public bool WasTriggered { get; private set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker
                .Entries<RepricingEvent>()
                .Any(x => x.Entity.Status == RepricingStatus.Applied))
            {
                WasTriggered = true;
                throw new InvalidOperationException(
                    "Simulated failure after SQL save, before transaction commit.");
            }

            return new ValueTask<int>(result);
        }
    }
}
