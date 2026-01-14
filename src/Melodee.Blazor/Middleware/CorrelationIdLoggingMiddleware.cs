using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace Melodee.Blazor.Middleware;

/// <summary>
/// Middleware to enrich Serilog logs with correlation ID from the current HTTP context.
/// Ensures every log statement includes the request correlation ID for traceability.
/// </summary>
public sealed class CorrelationIdLoggingMiddleware
{
    public const string CorrelationIdPropertyName = "CorrelationId";
    private readonly RequestDelegate _next;

    public CorrelationIdLoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.TraceIdentifier;

        using (LogContext.PushProperty(CorrelationIdPropertyName, correlationId, true))
        {
            await _next(context);
        }
    }
}

/// <summary>
/// Extension methods for registering correlation ID middleware.
/// </summary>
public static class CorrelationIdLoggingMiddlewareExtensions
{
    /// <summary>
    /// Adds middleware that enriches all Serilog log statements with the HTTP request correlation ID.
    /// </summary>
    public static IApplicationBuilder UseCorrelationIdLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<CorrelationIdLoggingMiddleware>();
    }
}
