using System.Diagnostics;

namespace AmazonRepricer.Api.Observability;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context)
    {
        var startedAt =
            Stopwatch.GetTimestamp();

        try
        {
            await _next(context);

            var elapsed =
                Stopwatch.GetElapsedTime(
                    startedAt);

            _logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs} ms.",
                context.Request.Method,
                context.Request.Path.Value
                    ?? string.Empty,
                context.Response.StatusCode,
                elapsed.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            var elapsed =
                Stopwatch.GetElapsedTime(
                    startedAt);

            _logger.LogError(
                exception,
                "HTTP {Method} {Path} failed in {ElapsedMs} ms.",
                context.Request.Method,
                context.Request.Path.Value
                    ?? string.Empty,
                elapsed.TotalMilliseconds);

            throw;
        }
    }
}
