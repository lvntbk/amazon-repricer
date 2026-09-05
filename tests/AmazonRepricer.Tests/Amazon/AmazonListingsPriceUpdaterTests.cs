using System.Net;
using System.Text;
using AmazonRepricer.Application.Pricing;
using AmazonRepricer.Infrastructure.Amazon;

namespace AmazonRepricer.Tests.Amazon;

public sealed class AmazonListingsPriceUpdaterTests
{
    [Fact]
    public async Task UpdatePriceAsync_ShouldSendExpectedPatchRequest()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.Accepted,
            """
            {
              "sku": "TEST/SKU + 001",
              "status": "ACCEPTED",
              "submissionId": "submission-test-001",
              "issues": []
            }
            """);

        var updater = CreateUpdater(handler);

        var result = await updater.UpdatePriceAsync(
            sellerId: "LOCAL-SELLER-001",
            sku: "TEST/SKU + 001",
            marketplaceId: "A33AVAJ2PDY3EV",
            productType: "PRODUCT",
            price: 1098.90m,
            currencyCode: "TRY");

        Assert.True(result.Accepted);
        Assert.Equal(
            "submission-test-001",
            result.SubmissionId);
        Assert.Empty(result.Issues);

        Assert.Equal(HttpMethod.Patch, handler.Method);

        Assert.Contains(
            "listings/2021-08-01/items/" +
            "LOCAL-SELLER-001/TEST%2FSKU%20%2B%20001",
            handler.RequestUri?.AbsoluteUri);

        Assert.Contains(
            "marketplaceIds=A33AVAJ2PDY3EV",
            handler.RequestUri?.Query);

        Assert.Equal(
            "test-access-token",
            handler.AccessToken);

        Assert.Contains(
            "\"productType\":\"PRODUCT\"",
            handler.RequestBody);

        Assert.Contains(
            "\"path\":\"/attributes/purchasable_offer\"",
            handler.RequestBody);

        Assert.Contains(
            "\"value_with_tax\":1098.90",
            handler.RequestBody);
    }

    [Fact]
    public async Task UpdatePriceAsync_ShouldReturnIssues_WhenNotAccepted()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "sku": "TEST-SKU-001",
              "status": "INVALID",
              "submissionId": "submission-test-002",
              "issues": [
                {
                  "code": "INVALID_PRICE",
                  "message": "Price is not valid.",
                  "severity": "ERROR"
                }
              ]
            }
            """);

        var updater = CreateUpdater(handler);

        var result = await updater.UpdatePriceAsync(
            "LOCAL-SELLER-001",
            "TEST-SKU-001",
            "A33AVAJ2PDY3EV",
            "PRODUCT",
            1098.90m,
            "TRY");

        Assert.False(result.Accepted);
        Assert.Single(result.Issues);
        Assert.Contains(
            "INVALID_PRICE",
            result.Issues[0]);
    }

    [Fact]
    public async Task UpdatePriceAsync_ShouldReportUncertainOutcome_WhenSuccessfulResponseCannotBeParsed()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.Accepted,
            "{ invalid-json");

        var updater = CreateUpdater(handler);

        var exception =
            await Assert.ThrowsAsync<HttpRequestException>(
                () => updater.UpdatePriceAsync(
                    "LOCAL-SELLER-001",
                    "TEST-SKU-001",
                    "A33AVAJ2PDY3EV",
                    "PRODUCT",
                    99m,
                    "TRY"));

        Assert.Contains(
            "outcome is uncertain",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task UpdatePriceAsync_GlobalSafetyGateBlocked_DoesNotSendHttpRequest()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.Accepted,
            """
            {
              "sku": "TEST-SKU-001",
              "status": "ACCEPTED",
              "submissionId": "should-not-be-submitted",
              "issues": []
            }
            """);

        var updater = CreateUpdater(
            handler,
            new StaticPriceUpdateSafetyGate(isAllowed: false));

        await updater.UpdatePriceAsync(
            "LOCAL-SELLER-001",
            "TEST-SKU-001",
            "A33AVAJ2PDY3EV",
            "PRODUCT",
            100m,
            "TRY");

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UpdatePriceAsync_ShouldRejectNonPositivePrice()
    {
        var handler = new FakeHttpMessageHandler(
            HttpStatusCode.OK,
            "{}");

        var updater = CreateUpdater(handler);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => updater.UpdatePriceAsync(
                "LOCAL-SELLER-001",
                "TEST-SKU-001",
                "A33AVAJ2PDY3EV",
                "PRODUCT",
                0m,
                "TRY"));

        Assert.Equal(0, handler.RequestCount);
    }

    private static AmazonListingsPriceUpdater CreateUpdater(
        HttpMessageHandler handler,
        IPriceUpdateSafetyGate? safetyGate = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://sandbox.test/")
        };

        return new AmazonListingsPriceUpdater(
            httpClient,
            new FakeAccessTokenProvider(),
            safetyGate
                ?? new StaticPriceUpdateSafetyGate(isAllowed: true));
    }

    private sealed class StaticPriceUpdateSafetyGate
        : IPriceUpdateSafetyGate
    {
        private readonly bool _isAllowed;

        public StaticPriceUpdateSafetyGate(bool isAllowed)
        {
            _isAllowed = isAllowed;
        }

        public Task<PriceUpdateSafetyGateResult> EvaluateAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new PriceUpdateSafetyGateResult(
                    _isAllowed,
                    _isAllowed
                        ? "Test gate allows updates."
                        : "Test gate blocks updates."));
        }
    }

    private sealed class FakeAccessTokenProvider
        : ILwaAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult("test-access-token");
        }
    }

    private sealed class FakeHttpMessageHandler
        : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _responseBody;

        public int RequestCount { get; private set; }
        public HttpMethod? Method { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? AccessToken { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        public FakeHttpMessageHandler(
            HttpStatusCode statusCode,
            string responseBody)
        {
            _statusCode = statusCode;
            _responseBody = responseBody;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;

            if (request.Headers.TryGetValues(
                    "x-amz-access-token",
                    out var values))
            {
                AccessToken = values.SingleOrDefault();
            }

            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(
                    cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(
                    _responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
