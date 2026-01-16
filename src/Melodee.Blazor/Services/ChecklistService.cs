using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Blazor.Services;

public sealed class ChecklistService
{
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly ICacheManager _cacheManager;
    private readonly SettingService _settingService;
    private readonly LibraryService _libraryService;
    private readonly IHostEnvironment _hostEnvironment;

    public ChecklistService(
        IMelodeeConfigurationFactory configurationFactory,
        ICacheManager cacheManager,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IHostEnvironment hostEnvironment)
    {
        _configurationFactory = configurationFactory;
        _cacheManager = cacheManager;
        _settingService = new SettingService(null!, cacheManager, configurationFactory, contextFactory);
        _libraryService = new LibraryService(null!, cacheManager, contextFactory, configurationFactory, null!, null!);
        _hostEnvironment = hostEnvironment;
    }

    public async Task<string> GenerateChecklistAsync(CancellationToken cancellationToken = default)
    {
        var config = await _configurationFactory.GetConfigurationAsync(cancellationToken);
        var siteName = config.GetValue<string>(SettingRegistry.SystemSiteName) ?? "Melodee";
        var baseUrl = config.GetValue<string>(SettingRegistry.SystemBaseUrl) ?? "http://localhost:5000";

        var librariesResult = await _libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        var libraries = librariesResult.Data.ToList();

        var inboundLib = libraries.FirstOrDefault(l => l.TypeValue == LibraryType.Inbound);
        var stagingLib = libraries.FirstOrDefault(l => l.TypeValue == LibraryType.Staging);
        var storageLibs = libraries.Where(l => l.TypeValue == LibraryType.Storage).ToList();

        var now = DateTime.UtcNow;
        var checklist = $@"# Melodee Setup Checklist

Generated: {now:yyyy-MM-dd HH:mm:ss} UTC
Site: {siteName}

> **Note**: This checklist is for internal use only. Please review all content for legal compliance before distribution.

## Overview

Welcome to Melodee! This checklist will help you get your media library organized and ready for use.

## Library Paths

| Library Type | Path |
|--------------|------|
| Inbound | `{GetPathDisplay(inboundLib?.Path)}` |
| Staging | `{GetPathDisplay(stagingLib?.Path)}` |
| Storage | `{string.Join(", ", storageLibs.Select(l => GetPathDisplay(l.Path)))}` |

## Getting Started

### 1. Add Media to Inbound Library

Copy your music files to the **Inbound** directory. Melodee will scan and process them.

**Recommended folder structure:**
```
{inboundLib?.Path ?? "/app/inbound"}/
  Artist Name/
    Album Name/
      01 Song Title.mp3
      02 Another Song.flac
      cover.jpg
```

### 2. Processing Workflow

1. Files are placed in the **Inbound** directory
2. Melodee scans and validates files
3. Metadata is fetched from MusicBrainz
4. Files are organized into the **Staging** area
5. After review, files move to **Storage** for playback

### 3. First Library Scan

After adding media, trigger a scan:

```bash
# Scan a specific library
mcli library scan --name Inbound

# Or scan all libraries
mcli library scan --all
```

### 4. Configure Search Engines (Optional)

Melodee can use various sources for album artwork and metadata:

```bash
# List available search engines
mcli search list

# Configure MusicBrainz
mcli settings set search.musicBrainz.enabled true
```

## Command Reference

### Library Management

| Command | Description |
|---------|-------------|
| `mcli library list` | List all configured libraries |
| `mcli library scan --name <name>` | Scan a specific library |
| `mcli library status <name>` | Check library scan status |

### Media Operations

| Command | Description |
|---------|-------------|
| `mcli doctor` | Run system diagnostics |
| `mcli backup export` | Export system configuration |
| `mcli player play` | Start playback (if configured) |

### Admin Commands

| Command | Description |
|---------|-------------|
| `mcli admin users` | Manage user accounts |
| `mcli settings list` | View all settings |
| `mcli settings set <key> <value>` | Update a setting |

## Next Steps

1. [x] Complete onboarding wizard
2. [ ] Add media to Inbound library
3. [ ] Run initial library scan
4. [ ] Review and organize your collection
5. [ ] Configure user accounts and permissions

## Troubleshooting

### Doctor Command

Run `mcli doctor` to diagnose common issues:

```bash
mcli doctor
```

Common fixes:
- Ensure library paths are writable
- Check database connectivity
- Verify disk space availability

### Log Files

Check logs for detailed error information:

```bash
# View recent logs
mcli logs --tail

# Search for specific errors
mcli logs --level error
```

## Legal Reminder

> **IMPORTANT**: This software is a tool for organizing personal media collections. Users are responsible for ensuring they have legal rights to rip, store, and play any media in their collection. Melodee does not provide or facilitate access to copyrighted content.

## Additional Resources

- Documentation: {baseUrl}/docs
- API Reference: {baseUrl}/swagger
- Source Code: https://github.com/anomalyco/melodee

---

*Generated by Melodee {now:yyyy-MM-dd}*
";

        return checklist;
    }

    private static string GetPathDisplay(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Not configured";
        }
        return path;
    }
}
