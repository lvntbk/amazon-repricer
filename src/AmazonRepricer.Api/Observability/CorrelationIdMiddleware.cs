namespace AmazonRepricer.Api.Observability;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName =
        "X-Correlation-ID";

    private const int MaxLength = 128;

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context)
    {
        var incomingCorrelationId =
            context.Request.Headers[HeaderName]
                .FirstOrDefault();

        var correlationId =
            IsValid(incomingCorrelationId)
                ? incomingCorrelationId!
                : Guid.NewGuid().ToString("N");

        context.TraceIdentifier =
            correlationId;

        context.Request.Headers[HeaderName] =
            correlationId;

        context.Response.OnStarting(
            () =>
            {
                context.Response.Headers[HeaderName] =
                    correlationId;

                return Task.CompletedTask;
            });

        using var scope =
            _logger.BeginScope(
                new Dictionary<string, object>
                {
                    ["CorrelationId"] =
                        correlationId
                });

        await _next(context);
    }

    private static bool IsValid(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > MaxLength)
        {
            return false;
        }

        return value.All(
            character =>
                char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.');
    }
}
