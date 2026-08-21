using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

const string accessToken = "local-sandbox-access-token";

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

app.Run();

static bool HasValidAccessToken(HttpRequest request)
{
    return request.Headers.TryGetValue(
               "x-amz-access-token",
               out var token) &&
           token == "local-sandbox-access-token";
}
