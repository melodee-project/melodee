using Melodee.Common.Models.Scripting;
using Melodee.Common.Services.ScriptEvaluation;

namespace Melodee.Cli.Services;

/// <summary>
/// CLI implementation of IScriptOrchestrationService that always returns the default "continue" result.
/// Script evaluation is only available in the Blazor web application context.
/// </summary>
public sealed class CliScriptOrchestrationService : IScriptOrchestrationService
{
    public Task<ScriptEvaluationResult> EvaluateScriptForEventAsync(
        string eventName,
        object context,
        int libraryId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ScriptEvaluationResult
        {
            Result = true,
            IsDefault = true,
            ErrorMessage = null
        });
    }
}
