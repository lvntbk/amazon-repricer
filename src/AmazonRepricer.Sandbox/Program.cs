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

app.Run();

static bool HasValidAccessToken(HttpRequest request)
{
    return request.Headers.TryGetValue(
               "x-amz-access-token",
               out var token) &&
           token == "local-sandbox-access-token";
}
