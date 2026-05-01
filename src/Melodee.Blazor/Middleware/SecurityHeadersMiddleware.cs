namespace Melodee.Blazor.Middleware;

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseMelodeeSecurityHeaders(this IApplicationBuilder app)
    {
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _environment;

    public SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.StartsWith("/scalar", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var response = context.Response;

        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        response.Headers["Permissions-Policy"] = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";

        if (!_environment.IsDevelopment())
        {
            response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-eval' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net; " +
            "img-src 'self' data: blob: https:; " +
            "font-src 'self' https://fonts.gstatic.com https://rsms.me https://cdn.jsdelivr.net data:; " +
            "connect-src 'self' wss: ws: https://cdn.jsdelivr.net; " +
            "worker-src 'self' blob: https://cdn.jsdelivr.net; " +
            "media-src 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'self';";

        await _next(context);
    }
}
