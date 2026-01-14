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
            "script-src 'self' 'unsafe-eval' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self'; " +
            "connect-src 'self' wss: ws:; " +
            "media-src 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'self';";

        await _next(context);
    }
}
