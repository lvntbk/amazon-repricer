using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

const string accessToken = "local-sandbox-access-token";

var listingPrices =
    new ConcurrentDictionary<string, SandboxListingPrice>();

app.MapGet("/", () => Results.Ok(new
{
    service = "Amazon Repricer Local SP-API Sandbox",
    status = "running"
}));

app.MapPost(
    "/auth/o2/token",
    async Task<IResult> (HttpRequest request) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new
            {
                error = "invalid_request",
                error_description = "Form content is required."
            });
        }

        var form = await request.ReadFormAsync();

        if (form["grant_type"] != "refresh_token")
        {
            return Results.BadRequest(new
            {
                error = "unsupported_grant_type",
                error_description = "Only refresh_token is supported."
            });
        }

        if (string.IsNullOrWhiteSpace(form["client_id"]) ||
            string.IsNullOrWhiteSpace(form["client_secret"]) ||
            string.IsNullOrWhiteSpace(form["refresh_token"]))
        {
            return Results.BadRequest(new
            {
                error = "invalid_grant",
                error_description = "Sandbox credentials are missing."
            });
        }

        return Results.Ok(new
        {
            access_token = accessToken,
            token_type = "bearer",
            expires_in = 3600
        });
    });

app.MapGet(
    "/sellers/v1/marketplaceParticipations",
    Results<Ok<object>, UnauthorizedHttpResult> (
        HttpRequest request) =>
    {
        if (!HasValidAccessToken(request))
            return TypedResults.Unauthorized();

        return TypedResults.Ok<object>(new
        {
            payload = new[]
            {
                new
                {
                    marketplace = new
                    {
                        id = "A33AVAJ2PDY3EV",
                        countryCode = "TR",
                        name = "Amazon.com.tr",
                        defaultCurrencyCode = "TRY",
                        defaultLanguageCode = "tr_TR",
                        domainName = "www.amazon.com.tr"
                    },
                    participation = new
                    {
                        isParticipating = true,
                        hasSuspendedListings = false
                    }
                }
            }
        });
    });


app.MapPost(
    "/batches/products/pricing/2022-05-01/items/competitiveSummary",
    async Task<IResult> (HttpRequest request) =>
    {
        if (!HasValidAccessToken(request))
            return Results.Unauthorized();

        var document =
            await JsonDocument.ParseAsync(request.Body);

        var requests = document.RootElement.GetProperty("requests");

        if (requests.GetArrayLength() is < 1 or > 20)
        {
            return Results.BadRequest(new
            {
                errors = new[]
                {
                    new
                    {
                        code = "InvalidInput",
                        message = "Requests must contain between 1 and 20 items."
                    }
                }
            });
        }

        var responses = requests
            .EnumerateArray()
            .Select(item =>
            {
                var asin = item.GetProperty("asin").GetString();
                var marketplaceId =
                    item.GetProperty("marketplaceId").GetString();

                return new
                {
                    status = new
                    {
                        statusCode = 200,
                        reasonPhrase = "Success"
                    },
                    body = new
                    {
                        asin,
                        marketplaceId,
                        featuredBuyingOptions = new[]
                        {
                            new
                            {
                                buyingOptionType = "New",
                                segmentedFeaturedOffers = new[]
                                {
                                    new
                                    {
                                        sellerId = "COMPETITOR-SELLER-001",
                                        condition = "New",
                                        fulfillmentType = "AFN",
                                        listingPrice = new
                                        {
                                            amount = 1099.90m,
                                            currencyCode = "TRY"
                                        },
                                        shippingOptions = new[]
                                        {
                                            new
                                            {
                                                shippingOptionType = "DEFAULT",
                                                price = new
                                                {
                                                    amount = 0m,
                                                    currencyCode = "TRY"
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };
            })
            .ToArray();

        return Results.Ok(new { responses });
    });


app.MapPatch(
    "/listings/2021-08-01/items/{sellerId}/{sku}",
    async Task<IResult> (
        string sellerId,
        string sku,
        HttpRequest request,
        CancellationToken cancellationToken) =>
    {
        if (!HasValidAccessToken(request))
            return Results.Unauthorized();

        var marketplaceId =
            request.Query["marketplaceIds"].ToString();

        if (string.IsNullOrWhiteSpace(marketplaceId))
        {
            return Results.BadRequest(new
            {
                errors = new[]
                {
                    new
                    {
                        code = "InvalidInput",
                        message = "marketplaceIds is required."
                    }
                }
            });
        }

        JsonDocument document;

        try
        {
            document = await JsonDocument.ParseAsync(
                request.Body,
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new
            {
                errors = new[]
                {
                    new
                    {
                        code = "InvalidJson",
                        message = "Request body is not valid JSON."
                    }
                }
            });
        }

        using (document)
        {
            if (!TryReadListingPrice(
                    document.RootElement,
                    out var productType,
                    out var bodyMarketplaceId,
                    out var currencyCode,
                    out var price,
                    out var validationError))
            {
                return Results.BadRequest(new
                {
                    errors = new[]
                    {
                        new
                        {
                            code = "InvalidInput",
                            message = validationError
                        }
                    }
                });
            }

            if (!string.Equals(
                    marketplaceId,
                    bodyMarketplaceId,
                    StringComparison.Ordinal))
            {
                return Results.BadRequest(new
                {
                    errors = new[]
                    {
                        new
                        {
                            code = "MarketplaceMismatch",
                            message =
                                "Query marketplace and body marketplace must match."
                        }
                    }
                });
            }

            var storageKey = CreateListingKey(
                sellerId,
                sku,
                marketplaceId);

            listingPrices[storageKey] =
                new SandboxListingPrice(
                    sellerId,
                    sku,
                    marketplaceId,
                    productType,
                    currencyCode,
                    price,
                    DateTime.UtcNow);

            return Results.Json(
                new
                {
                    sku,
                    status = "ACCEPTED",
                    submissionId = Guid.NewGuid().ToString("N"),
                    issues = Array.Empty<object>()
                },
                statusCode: StatusCodes.Status202Accepted);
        }
    });

app.MapGet(
    "/sandbox/listings/{sellerId}/{sku}",
    IResult (
        string sellerId,
        string sku,
        HttpRequest request) =>
    {
        var marketplaceId =
            request.Query["marketplaceIds"].ToString();

        if (string.IsNullOrWhiteSpace(marketplaceId))
        {
            return Results.BadRequest(new
            {
                error = "marketplaceIds is required."
            });
        }

        var storageKey = CreateListingKey(
            sellerId,
            sku,
            marketplaceId);

        if (!listingPrices.TryGetValue(
                storageKey,
                out var listing))
        {
            return Results.NotFound(new
            {
                error = "Listing price has not been updated.",
                sellerId,
                sku,
                marketplaceId
            });
        }

        return Results.Ok(listing);
    });

app.Run();

static bool HasValidAccessToken(HttpRequest request)
{
    return request.Headers.TryGetValue(
               "x-amz-access-token",
               out var token) &&
           token == "local-sandbox-access-token";
}


static string CreateListingKey(
    string sellerId,
    string sku,
    string marketplaceId)
{
    return $"{sellerId}|{sku}|{marketplaceId}";
}

static bool TryReadListingPrice(
    JsonElement root,
    out string productType,
    out string marketplaceId,
    out string currencyCode,
    out decimal price,
    out string error)
{
    productType = string.Empty;
    marketplaceId = string.Empty;
    currencyCode = string.Empty;
    price = 0;
    error = string.Empty;

    if (!root.TryGetProperty(
            "productType",
            out var productTypeElement))
    {
        error = "productType is required.";
        return false;
    }

    productType =
        productTypeElement.GetString() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(productType))
    {
        error = "productType cannot be empty.";
        return false;
    }

    if (!root.TryGetProperty("patches", out var patches) ||
        patches.ValueKind != JsonValueKind.Array)
    {
        error = "patches must be an array.";
        return false;
    }

    var pricePatch = patches
        .EnumerateArray()
        .FirstOrDefault(x =>
            x.TryGetProperty("path", out var path) &&
            path.GetString() ==
                "/attributes/purchasable_offer");

    if (pricePatch.ValueKind == JsonValueKind.Undefined ||
        !pricePatch.TryGetProperty("value", out var values) ||
        values.ValueKind != JsonValueKind.Array ||
        values.GetArrayLength() == 0)
    {
        error =
            "purchasable_offer patch value is required.";
        return false;
    }

    var offer = values[0];

    marketplaceId =
        offer.TryGetProperty(
            "marketplace_id",
            out var marketplaceElement)
            ? marketplaceElement.GetString() ?? string.Empty
            : string.Empty;

    currencyCode =
        offer.TryGetProperty(
            "currency",
            out var currencyElement)
            ? currencyElement.GetString() ?? string.Empty
            : string.Empty;

    if (string.IsNullOrWhiteSpace(marketplaceId))
    {
        error = "marketplace_id is required.";
        return false;
    }

    if (string.IsNullOrWhiteSpace(currencyCode))
    {
        error = "currency is required.";
        return false;
    }

    if (!offer.TryGetProperty(
            "our_price",
            out var ourPrices) ||
        ourPrices.ValueKind != JsonValueKind.Array ||
        ourPrices.GetArrayLength() == 0)
    {
        error = "our_price is required.";
        return false;
    }

    var ourPrice = ourPrices[0];

    if (!ourPrice.TryGetProperty(
            "schedule",
            out var schedules) ||
        schedules.ValueKind != JsonValueKind.Array ||
        schedules.GetArrayLength() == 0)
    {
        error = "price schedule is required.";
        return false;
    }

    var schedule = schedules[0];

    if (!schedule.TryGetProperty(
            "value_with_tax",
            out var priceElement) ||
        !priceElement.TryGetDecimal(out price) ||
        price <= 0)
    {
        error =
            "value_with_tax must be greater than zero.";
        return false;
    }

    return true;
}

sealed record SandboxListingPrice(
    string SellerId,
    string Sku,
    string MarketplaceId,
    string ProductType,
    string CurrencyCode,
    decimal Price,
    DateTime UpdatedAtUtc);
