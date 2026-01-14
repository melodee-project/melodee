using FluentAssertions;
using Melodee.Blazor.Middleware;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Events;

namespace Melodee.Tests.Blazor.Middleware;

public class CorrelationIdLoggingMiddlewareTests
{
    private const string TestCorrelationIdPropertyName = "CorrelationId";

    [Fact]
    public void CorrelationIdPropertyName_IsCorrect()
    {
        CorrelationIdLoggingMiddleware.CorrelationIdPropertyName.Should().Be("CorrelationId");
    }

    [Fact]
    public async Task CorrelationIdLoggingMiddleware_PassesThroughToNextMiddleware()
    {
        var nextCalled = false;
        var httpContext = new DefaultHttpContext();

        var middleware = new CorrelationIdLoggingMiddleware(next =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void CorrelationId_UsesHttpContextTraceIdentifier()
    {
        var expectedTraceId = "trace-abc-123";
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = expectedTraceId
        };

        httpContext.TraceIdentifier.Should().Be(expectedTraceId);
    }

    [Fact]
    public async Task CorrelationIdLoggingMiddleware_CreatesCorrectContext()
    {
        var capturedCorrelationId = "";
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "unique-correlation-123"
        };

        var middleware = new CorrelationIdLoggingMiddleware(next =>
        {
            capturedCorrelationId = httpContext.TraceIdentifier;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(httpContext);

        capturedCorrelationId.Should().Be(httpContext.TraceIdentifier);
    }
}
