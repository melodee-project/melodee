using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IBlazorEventScriptService
{
    Task<ScriptEvaluationResult> EvaluateUserRegistrationAsync(UserRegistrationContext context, CancellationToken cancellationToken = default);
    Task<ScriptEvaluationResult> EvaluateUserLoginAsync(UserLoginContext context, CancellationToken cancellationToken = default);
    Task<ScriptEvaluationResult> EvaluateUserProfileUpdateAsync(UserProfileUpdateContext context, CancellationToken cancellationToken = default);
    Task<ScriptEvaluationResult> EvaluatePlaylistCreateAsync(PlaylistCreateContext context, CancellationToken cancellationToken = default);
    Task<ScriptEvaluationResult> EvaluatePodcastChannelAddAsync(PodcastChannelAddContext context, CancellationToken cancellationToken = default);
    Task<ScriptEvaluationResult> EvaluateRequestCreateAsync(RequestCreateContext context, CancellationToken cancellationToken = default);
}

public sealed class BlazorEventScriptService : IBlazorEventScriptService
{
    private readonly IScriptOrchestrationService _orchestrationService;
    private readonly ILogger _logger;

    public BlazorEventScriptService(
        IScriptOrchestrationService orchestrationService,
        ILogger logger)
    {
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    public Task<ScriptEvaluationResult> EvaluateUserRegistrationAsync(UserRegistrationContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.UserRegistrationStart,
            context,
            cancellationToken);
    }

    public Task<ScriptEvaluationResult> EvaluateUserLoginAsync(UserLoginContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.UserLoginStart,
            context,
            cancellationToken);
    }

    public Task<ScriptEvaluationResult> EvaluateUserProfileUpdateAsync(UserProfileUpdateContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.UserProfileUpdateStart,
            context,
            cancellationToken);
    }

    public Task<ScriptEvaluationResult> EvaluatePlaylistCreateAsync(PlaylistCreateContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.PlaylistCreateStart,
            context,
            cancellationToken);
    }

    public Task<ScriptEvaluationResult> EvaluatePodcastChannelAddAsync(PodcastChannelAddContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.PodcastChannelAddStart,
            context,
            cancellationToken);
    }

    public Task<ScriptEvaluationResult> EvaluateRequestCreateAsync(RequestCreateContext context, CancellationToken cancellationToken = default)
    {
        return _orchestrationService.EvaluateScriptForEventAsync(
            ScriptEventNames.RequestCreateStart,
            context,
            cancellationToken);
    }
}
