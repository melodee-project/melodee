---
post_title: "Event Scripting Implementation"
author1: "steven"
post_slug: "event-scripting-implementation"
microsoft_alias: "n/a"
featured_image: "n/a"
categories:
  - "internal"
tags:
  - "implementation"
  - "event-scripting"
  - "roadmap"
ai_note: "AI-assisted"
summary: "Phased implementation plan for configuration-driven event scripting with Jint"
post_date: "2026-01-24"
---

## Phase Map

| Phase | Name | Status | Key Deliverables |
|-------|------|--------|------------------|
| 1 | Foundation | ✅ Completed | Jint integration, script evaluation service, error handling |
| 2 | Settings Infrastructure | ✅ Completed | Settings table integration, JSON schema, caching layer |
| 3 | Directory Processing Events | ✅ Completed | `directoryProcessingStart`, `directoryProcessingDelete` integration |
| 4 | Context Providers | ✅ Completed | Directory context builder, aggregate calculation utilities |
| 5 | Safety & Auditing | ✅ Completed | Safe deletion, path validation, audit logging |
| 6 | Blazor Events | ✅ Completed | User, playlist, podcast, share, request event hooks |
| 7 | Testing & Validation | ✅ Completed | Unit tests, validation service, dry-run mode |
| 8 | Documentation | ✅ Completed | Script reference docs, examples, admin guide |

## Phase 1: Foundation

### Objectives
- Establish Jint integration for script execution
- Create core script evaluation infrastructure
- Implement error handling with "proceed on error" defaults

### Deliverables

#### ScriptEvaluationService (`Melodee.Services/ScriptEvaluation/ScriptEvaluationService.cs`)
- `EvaluateScriptAsync(string scriptBody, object context, ScriptConfig config)` method
- Jint engine initialization with security constraints
- Timeout enforcement via `Timeout` option
- Statement limit enforcement via `MaxStatements` option
- CLR access disabled (strict mode)

#### ScriptConfig Model (`Melodee.Services/ScriptEvaluation/ScriptConfig.cs`)
```csharp
public class ScriptConfig
{
    public bool Enabled { get; init; } = true;
    public string Engine { get; init; } = "jint";
    public int TimeoutMs { get; init; } = 50;
    public int MaxStatements { get; init; } = 10000;
    public string? DefaultBody { get; init; }
    public List<ScriptOverrideConfig> Overrides { get; init; } = new();
}
```

#### ScriptOverrideConfig Model
```csharp
public class ScriptOverrideConfig
{
    public bool Enabled { get; init; } = true;
    public int? LibraryId { get; init; }
    public string? PathPrefix { get; init; }
    public string OnDeny { get; init; } = "skip";
    public string Body { get; init; } = string.Empty;
}
```

#### ScriptEvaluationResult Model
```csharp
public record ScriptEvaluationResult(
    bool Result,
    bool IsDefault,
    string? SelectedOverrideId,
    TimeSpan Duration,
    string? ErrorMessage
);
```

### Implementation Notes
- Jint options: `AllowSystemKeywords = false`, `Strict = true`
- All context objects must be plain DTOs, no CLR objects exposed
- Exceptions caught and logged, result defaults to `true`

---

## Phase 2: Settings Infrastructure

### Objectives
- Integrate with existing Settings table pattern
- Implement JSON schema for script configuration
- Build caching layer for compiled scripts
- Create override selection algorithm

### Deliverables

#### Settings Key Convention
- Base key pattern: `script.<eventName>`
- Example: `script.directoryProcessingStart`, `script.userLoginStart`

#### ScriptConfigurationStorage Service
- `GetScriptConfigAsync(string eventName)` method
- Reads from Settings table
- Deserializes JSON to ScriptConfig
- Returns null if key not found or disabled

#### ScriptCacheService
- Cache compiled Jint scripts by script body hash
- Invalidation on Settings change detection
- TTL-based eviction (default 5 minutes)
- Interface: `IScriptCacheService`

#### Override Selection Algorithm
```csharp
ScriptOverrideConfig? SelectOverride(
    ScriptConfig config,
    int libraryId,
    string relativePath)
{
    var candidates = config.Overrides
        .Where(o => o.Enabled)
        .ToList();

    var libraryMatch = candidates
        .Where(o => o.LibraryId == libraryId)
        .ToList();

    var pathMatches = candidates
        .Where(o => !string.IsNullOrEmpty(o.PathPrefix) &&
                    relativePath.StartsWith(o.PathPrefix, StringComparison.OrdinalIgnoreCase))
        .ToList();

    // Prefer library match over path match
    // Prefer longest path prefix
    // Fall back to default
}
```

#### JSON Schema Examples
```json
{
  "version": 1,
  "enabled": true,
  "engine": "jint",
  "timeoutMs": 50,
  "maxStatements": 10000,
  "default": {
    "body": "function check(ctx){ return true; }"
  },
  "overrides": [
    {
      "enabled": true,
      "libraryId": 1,
      "pathPrefix": "Incoming/",
      "onDeny": "delete",
      "body": "function check(ctx){ return ctx.mediaFilesCount >= 3; }"
    }
  ]
}
```

---

## Phase 3: Directory Processing Events

### Objectives
- Integrate script evaluation into `DirectoryProcessorToStagingService`
- Implement `directoryProcessingStart` gating
- Implement `directoryProcessingDelete` conditional deletion
- Respect `onDeny` action configuration

### Deliverables

#### DirectoryProcessorModifications
- Inject `IScriptEvaluationService` into `DirectoryProcessorToStagingService`
- Before processing each candidate directory:
  1. Build directory context
  2. Evaluate `directoryProcessingStart` script
  3. If `false`, evaluate `directoryProcessingDelete` script
  4. Apply configured `onDeny` action

#### Context Building for Directory Events
- `DirectoryContextBuilder` service
- Calculates aggregates from filesystem
- Normalizes paths
- Builds DTO for script consumption

#### OnDeny Action Handler
- `IDenyActionHandler` interface
- Implementations: `SkipHandler`, `DeleteHandler`, `QuarantineHandler`
- Delete handler enforces path constraints

### Integration Points

| Component | File | Changes |
|-----------|------|---------|
| DirectoryProcessorToStagingService | `Melodee.Services/DirectoryProcessing/DirectoryProcessorToStagingService.cs` | Add script evaluation before processing |
| Script Evaluation | `Melodee.Services/ScriptEvaluation/` | New project/directory |
| Settings Keys | `Melodee.Common/Data/Settings/` | Add script-related settings keys |

---

## Phase 4: Context Providers

### Objectives
- Build reusable context providers for different event types
- Calculate derived fields for script consumption
- Ensure consistent context shapes

### Deliverables

#### IDirectoryContextProvider
```csharp
public interface IDirectoryContextProvider
{
    DirectoryProcessingContext BuildContext(DirectoryInfo directory, Library library);
}
```

#### DirectoryProcessingContext DTO
```csharp
public record DirectoryProcessingContext(
    int LibraryId,
    string RelativePath,
    string DirectoryName,
    int TotalFilesCount,
    double TotalSizeMegabytes,
    string MostRecentModified,
    int MediaFilesCount,
    double TotalDurationMinutes,
    int[] TrackNumbers,
    bool HasTrackNumberGaps
);
```

#### Track Number Gap Detection
```csharp
public static bool DetectTrackNumberGaps(IEnumerable<int> trackNumbers)
{
    var sorted = trackNumbers.OrderBy(x => x).Distinct().ToList();
    if (!sorted.Any() || sorted[0] != 1)
        return true;

    for (int i = 1; i < sorted.Count; i++)
    {
        if (sorted[i] != sorted[i - 1] + 1)
            return true;
    }
    return false;
}
```

#### Duration Calculation
- Use TagLib-sharp for media file duration extraction
- Sum all media file durations
- Convert to minutes (double precision)

---

## Phase 5: Safety & Auditing

### Objectives
- Implement safe deletion with path constraints
- Add comprehensive audit logging
- Support dry-run and quarantine modes

### Deliverables

#### SafeDeleteService
- Path normalization and validation
- Root directory enforcement
- Cross-platform path handling

```csharp
public async Task<DeleteResult> DeleteDirectoryAsync(
    string relativePath,
    int libraryId,
    string? allowedRoot = null)
{
    var library = await _libraryRepository.GetAsync(libraryId);
    var inboundPath = _settingService.GetValue<string>($"library.inboundPath.{libraryId}");
    var safeRoot = allowedRoot ?? inboundPath;

    var fullPath = Path.GetFullPath(Path.Combine(safeRoot, relativePath));
    var rootPath = Path.GetFullPath(safeRoot);

    if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        throw new SecurityException("Path traversal attempt detected");

    // Proceed with deletion
}
```

#### ScriptAuditService
- Log all script evaluation decisions
- Include script hash for correlation (not body)
- Retention for minimum 30 days
- Query methods for Blazor page consumption

```csharp
public record ScriptAuditEntry(
    string EventName,
    int? LibraryId,
    string? RelativePath,
    string ScriptKey,
    string ScriptHash,
    string? OverrideId,
    bool Result,
    string Action,
    string? ErrorMessage,
    TimeSpan Duration,
    DateTime TimestampUtc
);
```

#### Audit Query Service
- Query methods for admin review in Blazor
- Filter by date range, library, event name

---

## Phase 6: Blazor Events

### Objectives
- Implement remaining event hooks for Blazor UI
- Create context providers for user, playlist, and other events
- Integrate with existing authentication and business logic

### Deliverables

#### Event Registration
| Event | Hook Point | Handler |
|-------|------------|---------|
| `userRegistrationStart` | User registration flow | `UserRegistrationScriptHandler` |
| `userLoginStart` | `/login` page | `LoginScriptHandler` |
| `userProfileUpdateStart` | Profile save | `ProfileUpdateScriptHandler` |
| `playlistCreateStart` | Playlist creation | `PlaylistCreateScriptHandler` |
| `podcastChannelAddStart` | Podcast subscription | `PodcastAddScriptHandler` |
| `shareCreateStart` | Share link creation | `ShareCreateScriptHandler` |
| `requestCreateStart` | Request submission | `RequestCreateScriptHandler` |

#### Context Providers

##### UserRegistrationContextProvider
```csharp
public record UserRegistrationContext(
    int UserNameLength,
    string EmailDomain,
    string ClientIp,
    string UserAgent,
    string Now
);
```

##### UserLoginContextProvider
```csharp
public record UserLoginContext(
    int? UserId,
    string[] Roles,
    string ClientIp,
    string UserAgent,
    string Now
);
```

##### PlaylistCreateContextProvider
```csharp
public record PlaylistCreateContext(
    int UserId,
    int NameLength,
    int InitialSongCount,
    string Now
);
```

#### Blazor Integration
- Add script evaluation to ASP.NET Core authorization handlers
- Create `ScriptAuthorizationHandler<TContext>` base class
- Integrate with existing authentication pipeline

---

## Phase 7: Testing & Validation

### Objectives
- Comprehensive unit test coverage
- Integration tests for directory processing
- Validation service for admins
- Dry-run mode for testing

### Deliverables

#### Unit Tests
| Test Category | Coverage |
|---------------|----------|
| Script evaluation | Success/failure paths, timeout, statement limits |
| Override selection | Library matching, path prefix, precedence |
| Context building | Track gaps, duration calculation |
| Safe deletion | Path traversal prevention |

#### Script Validation Service
- Service for validating scripts in Blazor admin pages
- Syntax checking and test execution

```csharp
public record ScriptValidationRequest(
    string EventName,
    string ScriptBody,
    object Context
);

public record ScriptValidationResult(
    bool IsValid,
    bool Result,
    double DurationMs,
    string? ErrorMessage
);
```

#### Dry-Run Mode
- Configuration flag: `script.dryRun.enabled`
- Log decisions without executing actions
- Useful for testing delete scripts

---

## Phase 8: Documentation

### Objectives
- User-facing documentation for Docsy site
- Script reference documentation for admins
- Example scripts for common scenarios
- Admin operational guide

### Deliverables

#### Docsy Documentation Structure
```
docs/
├── content/
│   └── en/
│       docs/
│           core-concepts/
│               scripting/
│                   index.md
│                   examples.md
│                   reference.md
```

#### Documentation Pages

##### Scripting Overview (`index.md`)
- What event scripting enables
- Use cases and examples
- Security considerations

##### Script Examples (`examples.md`)
- Minimum media files check
- Duration threshold
- Track number validation
- Library-specific rules
- Time-based restrictions

##### Script Reference (`reference.md`)
- All supported events
- Context object shapes for each event
- Configuration schema
- Error handling defaults

#### Admin Guide
- Creating and editing scripts via Settings
- Testing with validation endpoint
- Monitoring audit logs
- Rollback procedures

---

## Implementation Order

### Critical Path
1. Phase 1: Foundation (Jint, ScriptEvaluationService)
2. Phase 2: Settings Infrastructure (config storage, caching)
3. Phase 3: Directory Processing Events (primary use case)
4. Phase 4: Context Providers (directory context builder)

### Secondary Path
5. Phase 5: Safety & Auditing (delete safety, audit logs)
6. Phase 6: Blazor Events (additional event hooks)

### Completion Path
7. Phase 7: Testing & Validation (test coverage, validation API)
8. Phase 8: Documentation (user docs, examples)

---

## Dependencies

### Internal Dependencies
- `Melodee.Services.Settings` - Settings table access
- `Melodee.Services.Library` - Library information
- `Melodee.Services.FileSystem` - File operations

### External Dependencies
- `Jint` - JavaScript engine (NuGet)
- `TagLibSharp` - Media metadata (existing)
- `NodaTime` - Timestamp handling (existing)

### Infrastructure Requirements
- Settings table with JSON value support
- Audit log storage (database table)
- Cache infrastructure (memory cache or distributed)

---

## Backward Compatibility

### Settings Schema Versioning
- Current schema version: `1`
- Include `version` field in all script configurations
- Host implements migration path for future schema changes

### Script Format
- Standard `function check(ctx) { ... }` format
- Single boolean return value
- No breaking changes to context shapes without version bump

---

## Security Considerations

### Script Isolation
- Jint runs in-process with disabled CLR access
- No filesystem, network, or process APIs exposed
- Timeout and statement limits prevent infinite loops

### Admin-Only Access
- Script editing requires admin role
- Validation endpoint requires authentication
- Audit logs show who edited scripts (via Settings audit)

### Path Traversal Prevention
- All paths normalized and validated before operations
- Deletion constrained to configured safe roots
- Relative paths only, no absolute path acceptance

---

## Performance Targets

| Operation | Target |
|-----------|--------|
| Script evaluation (cold) | < 50ms |
| Script evaluation (cached) | < 10ms P99 |
| Directory context building | < 100ms per directory |
| Override selection | < 1ms |

### Optimization Strategies
- Cache compiled scripts until Settings change
- Pre-calculate directory aggregates during scan
- Batch Settings reads for multiple directories
- Async/await throughout to avoid blocking

---

## Rollout Plan

### 1. Feature Flag
- Wrap event scripting behind feature flag
- `feature.eventScripting.enabled` in Settings
- Default `false` until production validation

### 2. Phased Library Rollout
- Enable for one library first
- Monitor performance and audit logs
- Gradually expand to all libraries

### 3. Admin Training
- Document validation workflow
- Provide example scripts
- Train on audit log interpretation

---

## Open Questions

1. Should script validation be blocking (fail on syntax error) or non-blocking?
2. Should there be a maximum script body size?
3. Should script edits require confirmation for production libraries?
4. Should audit logs be stored in database or separate log store?

---

## References

- [Event Scripting Requirements](../requirements/event-scripting-requirements.md)
- [Jint Documentation](https://github.com/sebastienros/jint)
- [Settings Pattern](../architecture/pattern-settings.md)
- [Directory Processing](../services/directory-processing.md)
