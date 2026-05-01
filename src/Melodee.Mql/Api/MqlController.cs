using System.Diagnostics;
using Melodee.Common.Utility;
using Melodee.Mql.Api.Dto;
using Melodee.Mql.Interfaces;
using Melodee.Mql.Models;
using Melodee.Mql.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Melodee.Mql.Api;

/// <summary>
/// API controller for MQL parsing and validation.
/// </summary>
[ApiController]
[Route("api/v1/query")]
[ApiExplorerSettings(IgnoreApi = true)]
public class MqlController : ControllerBase
{
    private readonly IMqlTokenizer _tokenizer;
    private readonly IMqlParser _parser;
    private readonly IMqlValidator _validator;
    private readonly IMqlSuggestionService _suggestionService;
    private readonly IMqlExpressionCache _cache;
    private readonly MqlMetricsService _metricsService;
    private readonly ILogger<MqlController> _logger;

    private const int MaxQueryLength = 500;
    private const int ParseTimeoutMs = 5000;
    private const int RateLimitRequests = 10;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);

    private static readonly Dictionary<string, (int Count, DateTime WindowStart)> RequestCounts = new();
    private static readonly SemaphoreSlim RateLimitSemaphore = new(1, 1);

    public MqlController(
        IMqlTokenizer tokenizer,
        IMqlParser parser,
        IMqlValidator validator,
        IMqlSuggestionService suggestionService,
        IMqlExpressionCache cache,
        MqlMetricsService metricsService,
        ILogger<MqlController> logger)
    {
        _tokenizer = tokenizer;
        _parser = parser;
        _validator = validator;
        _suggestionService = suggestionService;
        _cache = cache;
        _metricsService = metricsService;
        _logger = logger;
    }

    /// <summary>
    /// Parses and validates an MQL query.
    /// </summary>
    /// <param name="request">The parse request containing entity type and query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The parse result with AST, normalized query, and any errors.</returns>
    [HttpPost("parse")]
    [Produces("application/json")]
    [Consumes("application/json")]
    public async Task<ActionResult<MqlParseResponse>> ParseAsync(
        [FromBody] MqlParseRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = "MQL_EMPTY_QUERY",
                    Message = "Query cannot be empty",
                    Timestamp = DateTime.UtcNow
                });
            }

            if (request.Query.Length > MaxQueryLength)
            {
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = "MQL_QUERY_TOO_LONG",
                    Message = $"Query exceeds maximum length of {MaxQueryLength} characters",
                    Timestamp = DateTime.UtcNow
                });
            }

            if (!await CheckRateLimitAsync(HttpContext.GetIpAddress() ?? "unknown"))
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new MqlErrorResponse
                {
                    ErrorCode = "RATE_LIMIT_EXCEEDED",
                    Message = $"Rate limit exceeded. Maximum {RateLimitRequests} requests per {RateLimitWindow.TotalMinutes} minute(s).",
                    Timestamp = DateTime.UtcNow
                });
            }

            var validationResult = _validator.Validate(request.Query, request.Entity);

            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Query validation failed for {Query}: {Errors}",
                    LogSanitizer.Sanitize(request.Query), string.Join(", ", validationResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

                var firstError = validationResult.Errors.First();
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = firstError.ErrorCode,
                    Message = firstError.Message,
                    Position = firstError.Position != null
                        ? new MqlPositionDto
                        {
                            Position = firstError.Position.Start,
                            Line = firstError.Position.Line,
                            Column = firstError.Position.Column,
                            Length = firstError.Position.End - firstError.Position.Start
                        }
                        : null,
                    Suggestions = firstError.Suggestions?
                        .Select(s => new MqlSuggestionDto
                        {
                            Text = s,
                            Type = "field",
                            Confidence = 0.8
                        })
                        .ToList() ?? new List<MqlSuggestionDto>(),
                    Timestamp = DateTime.UtcNow
                });
            }

            var tokens = _tokenizer.Tokenize(request.Query).ToList();

            using var timeoutCts = new CancellationTokenSource(ParseTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            MqlParseResult parseResult;
            try
            {
                parseResult = await Task.Run(() =>
                {
                    return _parser.Parse(tokens, request.Entity);
                }, linkedCts.Token);
            }
            catch (Exception)
            {
                if (timeoutCts.IsCancellationRequested)
                {
                    parseResult = MqlParseResult.Failed(new List<MqlError>
                    {
                        new("MQL_PARSE_TIMEOUT", "Query parsing timed out", null)
                    }, request.Query);
                }
                else
                {
                    throw;
                }
            }

            stopwatch.Stop();

            if (!parseResult.IsValid)
            {
                _logger.LogWarning("Query parsing failed for {Query}: {Errors}",
                    LogSanitizer.Sanitize(request.Query), string.Join(", ", parseResult.Errors.Select(e => LogSanitizer.Sanitize(e.Message))));

                var firstError = parseResult.Errors.First();
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = firstError.ErrorCode,
                    Message = firstError.Message,
                    Position = firstError.Position != null
                        ? new MqlPositionDto
                        {
                            Position = firstError.Position.Start,
                            Line = firstError.Position.Line,
                            Column = firstError.Position.Column,
                            Length = firstError.Position.End - firstError.Position.Start
                        }
                        : null,
                    Suggestions = firstError.Suggestions?
                        .Select(s => new MqlSuggestionDto
                        {
                            Text = s,
                            Type = "suggestion",
                            Confidence = 0.7
                        })
                        .ToList() ?? new List<MqlSuggestionDto>(),
                    Timestamp = DateTime.UtcNow
                });
            }

            _logger.LogInformation("Query parsed successfully in {TimeMs}ms: {Query}",
                stopwatch.ElapsedMilliseconds, LogSanitizer.Sanitize(request.Query));

            return Ok(new MqlParseResponse
            {
                NormalizedQuery = parseResult.NormalizedQuery ?? request.Query,
                Ast = parseResult.Ast?.ToDto(),
                Warnings = parseResult.Warnings,
                EstimatedComplexity = validationResult.ComplexityScore,
                Valid = true,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds
            });
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status499ClientClosedRequest, new MqlErrorResponse
            {
                ErrorCode = "MQL_REQUEST_CANCELLED",
                Message = "Request was cancelled by client",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error parsing query: {Query}", LogSanitizer.Sanitize(request.Query));

            return StatusCode(StatusCodes.Status500InternalServerError, new MqlErrorResponse
            {
                ErrorCode = "MQL_INTERNAL_ERROR",
                Message = "An unexpected error occurred while processing the query",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Health check endpoint for the MQL API.
    /// </summary>
    [HttpGet("health")]
    [Produces("application/json")]
    public ActionResult<Dictionary<string, string>> HealthCheck()
    {
        return Ok(new Dictionary<string, string>
        {
            ["status"] = "healthy",
            ["timestamp"] = DateTime.UtcNow.ToString("O")
        });
    }

    /// <summary>
    /// Returns performance metrics for the MQL API.
    /// </summary>
    [HttpGet("metrics")]
    [Produces("application/json")]
    public ActionResult<MqlMetricsResponse> GetMetrics()
    {
        var queryMetrics = _metricsService.GetSummary();
        var cacheMetrics = _metricsService.GetCacheMetrics(_cache);

        var response = new MqlMetricsResponse
        {
            Timestamp = DateTime.UtcNow,
            QueryMetrics = queryMetrics,
            CacheMetrics = cacheMetrics,
            Status = queryMetrics.ErrorRate > 0.05 ? "degraded" : "healthy"
        };

        return Ok(response);
    }

    /// <summary>
    /// Provides autocomplete suggestions for MQL queries.
    /// </summary>
    /// <param name="requestDto">The suggestion request containing entity type, query, and cursor position.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of suggestions based on the current query context.</returns>
    [HttpPost("suggest")]
    [Produces("application/json")]
    [Consumes("application/json")]
    public ActionResult<MqlSuggestionResponseDto> SuggestAsync(
        [FromBody] MqlSuggestionRequestDto requestDto,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(requestDto.Entity))
            {
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = "MQL_INVALID_ENTITY",
                    Message = "Entity type is required",
                    Timestamp = DateTime.UtcNow
                });
            }

            var normalizedEntity = requestDto.Entity.ToLowerInvariant();
            if (!MqlFieldRegistry.GetEntityTypes().Contains(normalizedEntity))
            {
                return BadRequest(new MqlErrorResponse
                {
                    ErrorCode = "MQL_UNKNOWN_ENTITY",
                    Message = $"Unknown entity type: {requestDto.Entity}. Valid types: {string.Join(", ", MqlFieldRegistry.GetEntityTypes())}",
                    Timestamp = DateTime.UtcNow
                });
            }

            var cursorPosition = Math.Max(0, Math.Min(requestDto.CursorPosition, requestDto.Query.Length));
            var result = _suggestionService.GetSuggestions(requestDto.Query ?? string.Empty, normalizedEntity, cursorPosition);

            _logger.LogDebug("Generated {Count} suggestions for query '{Query}' at position {Pos}",
                result.Suggestions.Count, LogSanitizer.Sanitize(requestDto.Query), cursorPosition);

            return Ok(new MqlSuggestionResponseDto
            {
                Suggestions = result.Suggestions.Select(s => new MqlSuggestionDto
                {
                    Text = s.Text,
                    Type = s.Type.ToString().ToLowerInvariant(),
                    Description = s.Description,
                    Confidence = s.Confidence
                }).ToList(),
                Query = result.Query,
                CursorPosition = result.CursorPosition,
                DetectedContext = result.DetectedContext,
                ProcessingTimeMs = result.ProcessingTimeMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating suggestions for query: {Query}", LogSanitizer.Sanitize(requestDto.Query));

            return StatusCode(StatusCodes.Status500InternalServerError, new MqlErrorResponse
            {
                ErrorCode = "MQL_SUGGESTION_ERROR",
                Message = "An error occurred while generating suggestions",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private static async Task<bool> CheckRateLimitAsync(string identifier)
    {
        await RateLimitSemaphore.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;

            if (RequestCounts.TryGetValue(identifier, out var record))
            {
                if (now - record.WindowStart > RateLimitWindow)
                {
                    RequestCounts[identifier] = (1, now);
                    return true;
                }

                if (record.Count >= RateLimitRequests)
                {
                    return false;
                }

                RequestCounts[identifier] = (record.Count + 1, record.WindowStart);
                return true;
            }

            RequestCounts[identifier] = (1, now);
            return true;
        }
        finally
        {
            RateLimitSemaphore.Release();
        }
    }
}

internal static class HttpContextExtensions
{
    public static string? GetIpAddress(this HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    }
}
