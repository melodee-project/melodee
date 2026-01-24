using Jint;
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

        var config = await _configurationService.GetScriptConfigAsync(request.EventName, cancellationToken)
                     ?? new ScriptConfig();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var effectiveScriptBody = NormalizeToCheckFunction(request.ScriptBody);
            var preparedScriptKey = ScriptHashing.Sha256Hex(effectiveScriptBody);
            var preparedScript = await _cacheService
                .GetOrCreatePreparedScriptAsync(preparedScriptKey, effectiveScriptBody, cancellationToken)
                .ConfigureAwait(false);

            var engine = new Engine(options =>
            {
                options.Strict = true;
                options.TimeoutInterval(TimeSpan.FromMilliseconds(config.TimeoutMs));
                options.MaxStatements(config.MaxStatements);
            });

            var scriptConfig = new
            {
                eventName = request.EventName,
                timeoutMs = config.TimeoutMs,
                maxStatements = config.MaxStatements
            };

            var contextValue = ScriptValueConverter.ToScriptValue(request.Context);
            var scriptConfigValue = ScriptValueConverter.ToScriptValue(scriptConfig);

            engine.Execute(preparedScript);
            var scriptResult = engine.Invoke("check", contextValue, scriptConfigValue);

            stopwatch.Stop();

            if (!scriptResult.IsBoolean())
            {
                return new ScriptValidationResult
                {
                    IsValid = false,
                    Result = true,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = "Script returned a non-boolean value"
                };
            }

            return new ScriptValidationResult
            {
                IsValid = true,
                Result = scriptResult.AsBoolean(),
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

    private static string NormalizeToCheckFunction(string scriptBody)
    {
        var trimmed = scriptBody.Trim();
        if (trimmed.Contains("function check", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return $"function check(ctx, scriptConfig) {{ return ({trimmed}); }}";
    }
}
