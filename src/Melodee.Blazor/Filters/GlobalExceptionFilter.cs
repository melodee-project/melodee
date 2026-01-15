using Melodee.Blazor.Controllers.Melodee.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Melodee.Blazor.Filters;

/// <summary>
/// Global exception filter that catches unhandled exceptions and returns structured ApiError responses.
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        // Only handle API requests
        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            var correlationId = context.HttpContext.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                ?? Guid.NewGuid().ToString();

            var error = new ApiError(
                ApiError.Codes.InternalError,
                "An unexpected error occurred",
                correlationId);

            context.Result = new ObjectResult(error)
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };

            context.ExceptionHandled = true;
        }
    }
}
