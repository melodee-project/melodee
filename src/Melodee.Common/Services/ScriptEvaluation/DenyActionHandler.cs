using Melodee.Common.Models.Scripting;
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
    Task<bool> ExecuteAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default);
}

public sealed class SkipDenyActionHandler : IDenyActionHandler
{
    public DenyAction ActionType => DenyAction.Skip;

    public Task<bool> ExecuteAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }
}

public interface ISafeDeleteService
{
    Task<bool> DeleteDirectoryAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default);
}

public sealed class SafeDeleteService : ISafeDeleteService
{
    private readonly LibraryService _libraryService;
    private readonly SettingService _settingService;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger _logger;

    public SafeDeleteService(
        LibraryService libraryService,
        SettingService settingService,
        IFileSystemService fileSystemService,
        ILogger logger)
    {
        _libraryService = libraryService;
        _settingService = settingService;
        _fileSystemService = fileSystemService;
        _logger = logger;
    }

    public async Task<bool> DeleteDirectoryAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var libraryResult = await _libraryService.GetAsync(libraryId, cancellationToken);
            if (!libraryResult.IsSuccess || libraryResult.Data == null)
            {
                _logger.Warning("Failed to get library {LibraryId} for safe delete", libraryId);
                return false;
            }

            var library = libraryResult.Data;
            var inboundPathSettingKey = $"library.inboundPath.{libraryId}";
            var inboundPathResult = await _settingService.GetValueAsync(inboundPathSettingKey, "", cancellationToken);
            var safeRoot = inboundPathResult.Data ?? library.Path;

            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(safeRoot, relativePath));
            var rootPath = System.IO.Path.GetFullPath(safeRoot);

            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Path traversal attempt detected: {FullPath} does not start with {RootPath}", fullPath, rootPath);
                return false;
            }

            if (!System.IO.Directory.Exists(fullPath))
            {
                _logger.Debug("Directory does not exist for deletion: {Path}", fullPath);
                return true;
            }

            _logger.Information("Safely deleting directory: {Path}", fullPath);
            System.IO.Directory.Delete(fullPath, true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to safely delete directory {Path}", relativePath);
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

    public Task<bool> ExecuteAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default)
    {
        return _safeDeleteService.DeleteDirectoryAsync(relativePath, libraryId, cancellationToken);
    }
}

public sealed class QuarantineDenyActionHandler : IDenyActionHandler
{
    public DenyAction ActionType => DenyAction.Quarantine;

    private readonly ISafeDeleteService _safeDeleteService;
    private readonly IFileSystemService _fileSystemService;
    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public QuarantineDenyActionHandler(
        ISafeDeleteService safeDeleteService,
        IFileSystemService fileSystemService,
        SettingService settingService,
        ILogger logger)
    {
        _safeDeleteService = safeDeleteService;
        _fileSystemService = fileSystemService;
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<bool> ExecuteAsync(string relativePath, int libraryId, CancellationToken cancellationToken = default)
    {
        try
        {
            var quarantinePathResult = await _settingService.GetValueAsync("script.quarantine.path", "", cancellationToken);
            if (string.IsNullOrEmpty(quarantinePathResult.Data))
            {
                _logger.Warning("Quarantine path not configured, falling back to delete");
                return await _safeDeleteService.DeleteDirectoryAsync(relativePath, libraryId, cancellationToken);
            }

            var libraryPathResult = await _settingService.GetValueAsync($"library.inboundPath.{libraryId}", "", cancellationToken);
            var libraryPath = libraryPathResult.Data ?? string.Empty;

            var sourceFullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(libraryPath, relativePath));
            var quarantineFullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(quarantinePathResult.Data!, relativePath));

            var rootPath = System.IO.Path.GetFullPath(quarantinePathResult.Data!);
            if (!quarantineFullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Path traversal attempt in quarantine: {Path}", quarantineFullPath);
                return false;
            }

            var quarantineDir = System.IO.Path.GetDirectoryName(quarantineFullPath);
            if (!string.IsNullOrEmpty(quarantineDir) && !System.IO.Directory.Exists(quarantineDir))
            {
                System.IO.Directory.CreateDirectory(quarantineDir);
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
            _logger.Error(ex, "Failed to quarantine directory {Path}", relativePath);
            return false;
        }
    }
}

public sealed class DenyActionHandlerFactory
{
    private readonly ISafeDeleteService _safeDeleteService;
    private readonly IFileSystemService _fileSystemService;
    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public DenyActionHandlerFactory(
        ISafeDeleteService safeDeleteService,
        IFileSystemService fileSystemService,
        SettingService settingService,
        ILogger logger)
    {
        _safeDeleteService = safeDeleteService;
        _fileSystemService = fileSystemService;
        _settingService = settingService;
        _logger = logger;
    }

    public IDenyActionHandler CreateHandler(string actionType)
    {
        return actionType.ToLowerInvariant() switch
        {
            "delete" => new DeleteDenyActionHandler(_safeDeleteService),
            "quarantine" => new QuarantineDenyActionHandler(_safeDeleteService, _fileSystemService, _settingService, _logger),
            _ => new SkipDenyActionHandler()
        };
    }
}
