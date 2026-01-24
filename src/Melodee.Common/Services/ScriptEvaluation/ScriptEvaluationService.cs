using Jint;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Services.Caching;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptEvaluationService
{
    Task<ScriptEvaluationResult> EvaluateScriptAsync(
        string scriptBody,
        object context,
        ScriptConfig config,
        CancellationToken cancellationToken = default);
}

public sealed class ScriptEvaluationService : IScriptEvaluationService
{
    private readonly ICacheManager _cacheManager;
    private readonly ILogger _logger;

    public ScriptEvaluationService(
        ILogger logger,
        ICacheManager cacheManager)
    {
        _logger = logger;
        _cacheManager = cacheManager;
    }

    public Task<ScriptEvaluationResult> EvaluateScriptAsync(
        string scriptBody,
        object context,
        ScriptConfig config,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (!config.Enabled)
            {
                return new ScriptEvaluationResult
                {
                    Result = true,
                    IsDefault = true,
                    ErrorMessage = null
                };
            }

            if (string.IsNullOrWhiteSpace(scriptBody))
            {
                return new ScriptEvaluationResult
                {
                    Result = true,
                    IsDefault = true,
                    ErrorMessage = "Script body is empty, defaulting to allow"
                };
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var engine = new Engine(options =>
                {
                    options.Strict = true;
                    options.TimeoutInterval(TimeSpan.FromMilliseconds(config.TimeoutMs));
                    options.MaxStatements(config.MaxStatements);
                });

                engine.SetValue("ctx", context);

                engine.Execute(scriptBody);

                var result = engine.Invoke("check", context);

                stopwatch.Stop();

                var boolResult = result.IsBoolean() && result.AsBoolean();

                return new ScriptEvaluationResult
                {
                    Result = boolResult,
                    IsDefault = false,
                    SelectedOverrideId = null,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.Error(ex, "Script evaluation failed for script body: {ScriptBodyHash}",
                    scriptBody.GetHashCode());

                return new ScriptEvaluationResult
                {
                    Result = true,
                    IsDefault = true,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = ex.Message
                };
            }
        }, cancellationToken);
    }
}
