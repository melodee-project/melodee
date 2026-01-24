using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public record ScriptValidationRequest
{
    public string EventName { get; init; } = string.Empty;
    public string ScriptBody { get; init; } = string.Empty;
    public object Context { get; init; } = null!;
}

public record ScriptValidationResult
{
    public bool IsValid { get; init; }
    public bool Result { get; init; }
    public double DurationMs { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IScriptValidationService
{
    Task<ScriptValidationResult> ValidateScriptAsync(ScriptValidationRequest request, CancellationToken cancellationToken = default);
}

public sealed class ScriptValidationService : IScriptValidationService
{
    private readonly IScriptConfigurationService _configurationService;
    private readonly IScriptCacheService _cacheService;
    private readonly ILogger _logger;

    public ScriptValidationService(
        IScriptConfigurationService configurationService,
        IScriptCacheService cacheService,
        ILogger logger)
    {
        _configurationService = configurationService;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<ScriptValidationResult> ValidateScriptAsync(ScriptValidationRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptBody))
        {
            return new ScriptValidationResult
            {
                IsValid = true,
                Result = true,
                ErrorMessage = null
            };
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var scriptHash = request.ScriptBody.GetHashCode().ToString();
            var engine = await _cacheService.GetOrCreateEngineAsync(scriptHash, request.ScriptBody, cancellationToken);

            var scriptResult = engine.Invoke("check", request.Context);

            stopwatch.Stop();

            var boolResult = scriptResult.ToObject() is true;

            return new ScriptValidationResult
            {
                IsValid = true,
                Result = boolResult,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Debug(ex, "Script validation failed for event {EventName}", request.EventName);

            return new ScriptValidationResult
            {
                IsValid = false,
                Result = true,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message
            };
        }
    }
}
