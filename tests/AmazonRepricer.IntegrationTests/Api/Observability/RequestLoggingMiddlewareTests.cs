using AmazonRepricer.Api.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AmazonRepricer.IntegrationTests.Api.Observability;

public sealed class RequestLoggingMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_LogsStructuredRequestCompletion()
    {
        var context =
            new DefaultHttpContext();

        context.Request.Method =
            HttpMethods.Get;

        context.Request.Path =
            "/health/live";

        var logger =
            new CapturingLogger<
                RequestLoggingMiddleware>();

        var middleware =
            new RequestLoggingMiddleware(
                async httpContext =>
                {
                    httpContext.Response.StatusCode =
                        StatusCodes.Status204NoContent;

                    await Task.CompletedTask;
                },
                logger);

        await middleware.InvokeAsync(
            context);

        var entry =
            Assert.Single(
                logger.Entries);

        Assert.Equal(
            LogLevel.Information,
            entry.LogLevel);

        AssertProperty(
            entry.State,
            "Method",
            HttpMethods.Get);

        AssertProperty(
            entry.State,
            "Path",
            "/health/live");

        AssertProperty(
            entry.State,
            "StatusCode",
            StatusCodes.Status204NoContent);

        Assert.Contains(
            entry.State,
            item =>
                item.Key == "ElapsedMs");
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestFails_LogsStructuredFailureAndRethrows()
    {
        var context =
            new DefaultHttpContext();

        context.Request.Method =
            HttpMethods.Post;

        context.Request.Path =
            "/api/products";

        var logger =
            new CapturingLogger<
                RequestLoggingMiddleware>();

        var middleware =
            new RequestLoggingMiddleware(
                _ =>
                    throw new InvalidOperationException(
                        "test failure"),
                logger);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                middleware.InvokeAsync(
                    context));

        var entry =
            Assert.Single(
                logger.Entries);

        Assert.Equal(
            LogLevel.Error,
            entry.LogLevel);

        Assert.IsType<
            InvalidOperationException>(
            entry.Exception);

        AssertProperty(
            entry.State,
            "Method",
            HttpMethods.Post);

        AssertProperty(
            entry.State,
            "Path",
            "/api/products");

        Assert.Contains(
            entry.State,
            item =>
                item.Key == "ElapsedMs");
    }

    private static void AssertProperty(
        IReadOnlyList<
            KeyValuePair<string, object?>>
            state,
        string key,
        object expected)
    {
        Assert.Contains(
            state,
            item =>
                item.Key == key &&
                Equals(
                    item.Value,
                    expected));
    }

    private sealed class CapturingLogger<T>
        : ILogger<T>
    {
        public List<LogEntry>
            Entries { get; } = [];

        public IDisposable? BeginScope<TState>(
            TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(
            LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is
                IEnumerable<
                    KeyValuePair<string, object?>>
                properties)
            {
                Entries.Add(
                    new LogEntry(
                        logLevel,
                        exception,
                        properties.ToList()));
            }
        }

        public sealed record LogEntry(
            LogLevel LogLevel,
            Exception? Exception,
            IReadOnlyList<
                KeyValuePair<string, object?>>
                State);

        private sealed class NullScope
            : IDisposable
        {
            public static NullScope Instance { get; } =
                new();

            public void Dispose()
            {
            }
        }
    }
}
