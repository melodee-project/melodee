using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public enum DenyAction
{
    Skip,
    Delete,
    Quarantine
}

public interface IDenyActionHandler
{
    DenyAction ActionType { get; }
    Task<bool> ExecuteAsync(string directoryPath, CancellationToken cancellationToken = default);
}

public sealed class SkipDenyActionHandler : IDenyActionHandler
{
    public DenyAction ActionType => DenyAction.Skip;

    public Task<bool> ExecuteAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}

public interface ISafeDeleteService
{
    Task<bool> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
}

public sealed class SafeDeleteService : ISafeDeleteService
{
    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public SafeDeleteService(
        SettingService settingService,
        ILogger logger)
    {
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<bool> DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = System.IO.Path.GetFullPath(directoryPath);

            if (!System.IO.Directory.Exists(fullPath))
            {
                _logger.Debug("Directory does not exist for deletion: {Path}", fullPath);
                return true;
            }

            var dryRunResult = await _settingService.GetValueAsync("script.dryRun.enabled", false, cancellationToken);
            if (dryRunResult.IsSuccess && dryRunResult.Data == true)
            {
                _logger.Information("Dry-run enabled; would delete directory: {Path}", fullPath);
                return true;
            }

            _logger.Information("Deleting directory: {Path}", fullPath);
            System.IO.Directory.Delete(fullPath, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete directory {Path}", directoryPath);
            return false;
        }
    }
}

public sealed class DeleteDenyActionHandler : IDenyActionHandler
{
    public DenyAction ActionType => DenyAction.Delete;

    private readonly ISafeDeleteService _safeDeleteService;

    public DeleteDenyActionHandler(ISafeDeleteService safeDeleteService)
    {
        _safeDeleteService = safeDeleteService;
    }

    public Task<bool> ExecuteAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        return _safeDeleteService.DeleteDirectoryAsync(directoryPath, cancellationToken);
    }
}

public sealed class QuarantineDenyActionHandler : IDenyActionHandler
{
    public DenyAction ActionType => DenyAction.Quarantine;

    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public QuarantineDenyActionHandler(
        SettingService settingService,
        ILogger logger)
    {
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var quarantinePathResult = await _settingService.GetValueAsync("script.quarantine.path", "", cancellationToken);
            if (string.IsNullOrEmpty(quarantinePathResult.Data))
            {
                _logger.Warning("Quarantine path not configured, skipping quarantine for: {Path}", directoryPath);
                return false;
            }

            var sourceFullPath = System.IO.Path.GetFullPath(directoryPath);
            var directoryName = System.IO.Path.GetFileName(sourceFullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            var quarantineFullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(quarantinePathResult.Data!, directoryName));

            var rootPath = System.IO.Path.GetFullPath(quarantinePathResult.Data!);
            if (!quarantineFullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Path traversal attempt in quarantine: {Path}", quarantineFullPath);
                return false;
            }

            if (!System.IO.Directory.Exists(rootPath))
            {
                System.IO.Directory.CreateDirectory(rootPath);
            }

            _logger.Information("Quarantining directory from {Source} to {Destination}", sourceFullPath, quarantineFullPath);

            if (System.IO.Directory.Exists(sourceFullPath))
            {
                System.IO.Directory.Move(sourceFullPath, quarantineFullPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to quarantine directory {Path}", directoryPath);
            return false;
        }
    }
}

public sealed class DenyActionHandlerFactory
{
    private readonly ISafeDeleteService _safeDeleteService;
    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public DenyActionHandlerFactory(
        ISafeDeleteService safeDeleteService,
        SettingService settingService,
        ILogger logger)
    {
        _safeDeleteService = safeDeleteService;
        _settingService = settingService;
        _logger = logger;
    }

    public IDenyActionHandler CreateHandler(string actionType)
    {
        return actionType.ToLowerInvariant() switch
        {
            "delete" => new DeleteDenyActionHandler(_safeDeleteService),
            "quarantine" => new QuarantineDenyActionHandler(_settingService, _logger),
            _ => new SkipDenyActionHandler()
        };
    }
}
