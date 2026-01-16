# Phased Implementation Guide (Onboarding Wizard + Unified Doctor)

This guide breaks implementation into discrete phases meant for coding agents. Each phase has explicit deliverables and
definition-of-done criteria to minimize design work during implementation.

> Principle: **One Doctor, many UIs**. The check engine and definitions live in a shared project; Blazor and CLI are
> presentation layers.

---

## Phase Map

- [x] Phase 0 — Baseline + inventory required items
- [x] Phase 1 — CLI system export (backup)
- [x] Phase 2 — Extract/standardize Doctor into shared service
- [x] Phase 3 — Add shared SetupCheck API + models
- [x] Phase 4 — Blazor startup gating + route guard
- [x] Phase 5 — Implement onboarding wizard UI skeleton (COMPLETED)
- [x] Phase 6 — Implement wizard steps (branding, security, paths, admin) (COMPLETED)
- [x] Phase 7 — Download checklist + final verification (COMPLETED)
- [x] Phase 8 — Admin Dashboard JSON export/import (COMPLETED)
- [x] Phase 9 — Refactor `mcli doctor` to use shared Doctor (COMPLETED)
- [x] Phase 10 — Tests + "definition of done" hardening (COMPLETED)
~~~~
---

## Phase 0 — Baseline + inventory required items (COMPLETED)

### Deliverables~~~~
- [x] Create a definitive list of:
  - [x] required Settings keys (blocking) for onboarding completion~~~~
  - [x] required library types (blocking) for onboarding completion
- [x] Confirm current defaults/seed values:
  - [x] `system.baseUrl` default: `MelodeeConfiguration.RequiredNotSetValue`
  - [x] `system.siteName` default: `"Melodee"`
  - [x] library defaults: `/app/inbound`, `/app/staging`, `/app/storage` (and others)

### Implementation notes (no design work)
- Required placeholder rule: any `Settings.Value == MelodeeConfiguration.RequiredNotSetValue` is required+blocking.
- Explicit required keys (even if not placeholder-based):
  - `security.secretKey`
  - `system.onboardingCompletedAt` (completion marker)

### Definition of done
- [x] The required key/type list is captured in code as constants/definitions (not a wiki decision).

**Implementation**: Created `src/Melodee.Common/Constants/OnboardingRequirements.cs` containing:
- `RequiredLibraryTypes[]`: Inbound, Staging, Storage
- `RequiredSettingsKeys[]`: system.baseUrl, system.siteName, security.secretKey, system.onboardingCompletedAt
- `DefaultLibraryPaths`: Default paths for all library types
- `DefaultSettingValues`: Default values for system settings

Also added `SettingRegistry.SystemOnboardingCompletedAt = "system.onboardingCompletedAt"` to SettingRegistry.cs.

---

## Phase 1 — CLI system export (backup) (COMPLETED)

### Deliverables
- [x] Add a new CLI command to generate a system export (exact naming fixed to avoid churn):
  - [x] `mcli backup export`
- [x] Export format is JSON and must match the UI/onboarding import schema exactly (same schema version + JSON shape).
- [x] Export includes, at minimum:
  - [x] Settings (all rows)
  - [x] Libraries (all rows)
- [x] Export supports:
  - [x] `--output <path>` to write to a file
  - [x] `--stdout` to write to stdout (machine-friendly)
  - [x] `--redact-secrets` (explicit) to replace secret values with a sentinel marker
- [x] Export output must be deterministic for diffing:
  - [x] Settings sorted alphabetically by key
  - [x] Libraries sorted by type then name (alphabetical)
  - [x] consistent indentation and casing

### Implementation notes
- This phase can be parallelized with Phase 0.
- Redaction rules (fixed):
  - redact `security.secretKey`
  - redact any key containing `secret`, `token`, or `password` (case-insensitive) unless explicitly allow-listed
- Add a CLI help section explaining that this is not a SQL backup and may include secrets.

### Definition of done
- [x] `mcli backup export --output export.json` produces a valid JSON export that the UI/onboarding import can consume.
- [x] Export is deterministic and produces identical output for identical input.

**Implementation**: Created `src/Melodee.Cli/Command/BackupExportCommand.cs` with:
- `mcli backup export` command supporting `--output`, `--stdout`, `--redact-secrets`, and `--raw` options
- JSON export with schema version, exportedAt timestamp, settings (sorted by key), and libraries (sorted by type then name)
- Secret redaction for keys containing "secret", "token", or "password" (case-insensitive)
- Settings class: `src/Melodee.Cli/CommandSettings/BackupExportSettings.cs`

---

## Phase 2 — Extract/standardize Doctor into shared service (COMPLETED)

### Deliverables
- [x] Move Doctor models/interfaces out of `src/Melodee.Blazor/Services` into a shared location (target suggestion):
  - [x] `src/Melodee.Common/Services/Doctor/`
- [x] Create a single shared interface used by both hosts:
  - [x] `IDoctorService` (shared)
  - [x] `DoctorCheckResults`, `DoctorCheckResult`, and any supporting records/enums (shared)
- [x] Update `Melodee.Blazor` to reference the shared types and wire DI to the shared implementation.

### Implementation notes
- Split checks into:
  - **Core checks** (DB + settings + libraries): must not depend on ASP.NET or Blazor types.
  - **Host checks** (optional): can use `IWebHostEnvironment`, `IHttpContextAccessor`, `ISchedulerFactory`, Serilog
    config. These may live in `Melodee.Blazor` but must reuse shared result models and IDs.
- Use stable check IDs/names. Do not change semantics between CLI and Blazor for shared checks.

### Definition of done
- [x] `Melodee.Blazor` compiles using the shared doctor types.
- [x] Shared doctor service can run core checks without ASP.NET dependencies.

**Implementation**: Created shared Doctor types in `src/Melodee.Common/Services/Doctor/`:
- `IDoctorService.cs` - Core interface with `RunCoreChecksAsync`, `RunConfigurationCheckAsync`, `RunDatabaseCheckAsync`, `RunLibraryPathCheckAsync`, `RunConfigurableServicesCheckAsync`
- `DoctorCheckModels.cs` - Shared models: `DoctorCheckResults`, `DoctorCheckResult`, `LibraryPathResult`, `ConfigurableServiceResult`, `DiskSpaceStatus`, `DiskSpaceInfo`, `SearchEngineApiKeyInfo`, `SerilogLogPathInfo`, `ConnectionStringInfo`, `EnvironmentVariableInfo`
- `DoctorServiceBase.cs` - Base implementation with core checks (Configuration, Database, LibraryPaths, ConfigurableServices)

Updated Blazor `DoctorService.cs` to inherit from `DoctorServiceBase` and add Blazor-specific checks (MusicBrainz DB, ArtistSearchEngine DB, Serilog logging, Disk space, Path overlap, Search engine API keys, SMTP, JWT, HTTPS, Admin password, Scheduler, FFmpeg, Memory, Temp directory, Database latency, Jukebox, Podcast).

Updated `IDoctorService.cs` in Blazor to extend the shared interface and add Blazor-specific `BlazorDoctorCheckResults` type.

---

## Phase 3 — Add shared SetupCheck API + models (COMPLETED)

### Deliverables
- [x] Add a shared "setup readiness" API surface (names fixed to avoid design churn):
  - [x] `Task<SetupStatus> SetupCheckAsync(...)`
  - [x] `SetupStatus` includes `IsReady`, `Items`, and `BlockingItems`
  - [x] `SetupItem` includes `Id`, `Name`, `Severity`, `Success`, `Details`, `Remediation`, `FixRoute` (e.g., `/onboarding/...`)
- [x] Implement SetupCheck blocking rules exactly as in `design/requirements/onboarding-wizard.md`.

### Implementation notes
- Compute missing required settings by querying `Settings`:
  - any row where `Value == MelodeeConfiguration.RequiredNotSetValue`
  - plus required explicit keys: `system.baseUrl`, `system.siteName`, `security.secretKey`
- Compute library readiness using `LibraryService`:
  - verify Inbound + Staging exist and are writable
  - verify at least one Storage exists and is writable
  - normalize and check overlap (case-insensitive, directory separators normalized)
  - resolve symbolic links before overlap check
  - reject paths containing traversal sequences (`..`, `.`)
- Add a "Recommended" (non-blocking) check for sufficient disk space (e.g., < 1GB) on library volumes.
- Never include secrets in `Details`. Use masked values where needed.

### Definition of done
- SetupCheck returns deterministic results for all required setup checks and can be consumed by UI/CLI.
- Path validation includes symlink resolution and traversal rejection.

---

## Phase 4 — Blazor startup gating + route guard

### Deliverables
- Add onboarding routing:
  - `/onboarding` (wizard entry)
  - `/onboarding/blocking` (cannot compute setup status)
- Implement onboarding required detection:
  - `OnboardingRequired = !HasOnboardingCompletedAt || !SetupCheck.IsReady`
- Implement an "unmissable" guard:
  - If onboarding required, force navigation to `/onboarding` and hide normal navigation chrome.
- Add blocking screen UX for `/onboarding/blocking`:
  - Clear error message (e.g., "Cannot connect to database")
  - Retry button (re-attempt connection)
  - Link to relevant documentation or support resources

### Implementation notes
- Integrate guard in a single place that affects all routes:
  - `src/Melodee.Blazor/Components/Layout/MainLayout.razor` (or an equivalent top-level layout component)
  - or adjust `src/Melodee.Blazor/Components/Routes.razor` to include an `OnNavigateAsync` guard (if refactoring Router)
- Guard must avoid redirect loops:
  - allow `/account/login`, `/account/logout`, `/onboarding`, `/onboarding/blocking`
- Compute SetupCheck once per app start and cache (scoped state) with a "refresh" action the wizard can invoke.
- Wizard must call `SetupCheckAsync` refresh after each step that mutates configuration to ensure up-to-date status.

### Definition of done
- A failing SetupCheck reliably redirects to onboarding without flicker/loop.
- A passing SetupCheck and completed marker reliably routes to the normal home/dashboard.
- Blocking screen displays clear error message with retry and support link.
- SetupCheck is refreshed after wizard step mutations.

---

## Phase 5 — Implement onboarding wizard UI skeleton

### Deliverables
- Create onboarding components/pages under `src/Melodee.Blazor/Components/Pages/Onboarding/`.
- Implement:
  - stepper/progress indicator
  - shared wizard state model (current step, setup status snapshot, validation errors)
  - Back/Next navigation (Back allows revisiting previous steps)
  - auto-advance logic to skip forward to the first incomplete/blocking step upon wizard entry or re-entry.

### Implementation notes
- Use existing UI library patterns (Radzen components, localization via `L("...")` pattern used elsewhere).
- Persist changes immediately per step (no giant "Save at end"), but validate before allowing Next.
- Ensure all wizard strings are localized using the `L("...")` pattern.

### Definition of done
- Wizard renders and can navigate across placeholder steps without implementing mutations yet.
- All wizard text uses localization pattern.

**Implementation**: Created wizard components under `src/Melodee.Blazor/Components/Pages/Onboarding/`:
- `Index.razor` - Main wizard page with stepper navigation, progress bar, and auto-advance logic
- `Blocking.razor` - Blocking screen for when setup status cannot be determined

Created step components under `src/Melodee.Blazor/Components/Onboarding/`:
- `OnboardingWelcome.razor` - Welcome step with optional import functionality
- `OnboardingBranding.razor` - Branding step (site name, base URL)
- `OnboardingSecurity.razor` - Security step (secret key generation)
- `OnboardingPaths.razor` - Library paths step (Inbound, Staging, Storage)
- `OnboardingAdmin.razor` - Admin account creation step
- `OnboardingVerify.razor` - Verification step before completion
- `OnboardingGuard.razor` - Route guard that redirects to onboarding if required
- `OnboardingRedirect.razor` - Redirect component for onboarding flow

Added `OnboardingStateService.cs` for managing wizard state and setup checks.

Added all localization strings to `SharedResources.resx`.

---

## Phase 6 — Implement wizard steps (branding, security, paths, admin)

### Deliverables
Implement these steps with exact behaviors:

0) Import system export (optional fast path)
- Add an optional "Import existing system export" action in the onboarding wizard Welcome step.
- The import consumes the same JSON schema produced by `mcli backup export` and the Admin Dashboard export.
- Import applies Settings + Libraries and then re-runs SetupCheck:
  - on success, auto-skip satisfied steps
  - on partial success, show a summary and continue with remaining steps
- Import must be transactional (all or nothing).

0b) Create first admin (conditional)
- If no admin exists in the database, provide a "Create first admin" step.
- This step collects admin credentials (username, password) and creates the admin user.
- Uses existing user management services to ensure consistency.

1) Branding
- Uses `SettingService.UpdateAsync(...)` (or existing setting editing patterns) to update:
  - `system.siteName`
  - `system.baseUrl` (trimmed; validated as absolute http/https)
- Implement an optional "Test Reachability" button for `system.baseUrl`.
- Display settings overridden by environment variables as read-only with a "Set via Environment" indicator.

2) Security secret key
- If `security.secretKey` missing/invalid, generate and persist.
- Generation algorithm (fixed):
  - `RandomNumberGenerator.GetBytes(48)` then Base64 encode (yields 64 chars)
- Store key in `Settings` table key `security.secretKey`.
- UI shows key only once at creation time and then masks it.
- Provide "Regenerate" option with confirmation (for future use after setup).

3) Library paths
- Use `LibraryService` to update:
  - Inbound path
  - Staging path
  - Storage path (first storage library or prompt to create one if none exist)
- Provide recommended defaults based on environment:
  - Container: `/app/inbound`, `/app/staging`, `/app/storage`
  - Windows: `C:\Melodee\Inbound`, `C:\Melodee\Staging`, `C:\Melodee\Storage`
  - Linux/macOS: `/var/lib/melodee/inbound`, `/var/lib/melodee/staging`, `/var/lib/melodee/storage`
- Provide actions:
  - Browse/select path (if supported) or manual entry
  - Create directory
  - Test write permissions
- Enforce overlap rules before allowing Next.
- Resolve symlinks to canonical paths before validation.
- Reject paths containing traversal sequences (`..`, `.`).

### Definition of done
- Running the wizard end-to-end can satisfy all blocking requirements except the final completion marker.
- Wizard includes admin creation when no admin exists.
- Import is transactional.
- Path validation includes symlink resolution and traversal rejection.

**Implementation**: Created `src/Melodee.Common/Services/SystemImportService.cs` for transactional import of settings and libraries from JSON exports. Implemented wizard step functionality:

- `OnboardingWelcome.razor`: Added file upload and JSON import functionality that applies import data and refreshes setup status
- `OnboardingBranding.razor`: Implemented site name and base URL editing with URL validation and reachability testing
- `OnboardingSecurity.razor`: Implemented secret key generation (48 bytes, Base64 encoded) with display-once and regenerate confirmation
- `OnboardingPaths.razor`: Implemented library path configuration with path overlap detection (including symlink resolution), traversal rejection, directory creation, and write permission testing. Creates missing library records automatically.
- `OnboardingAdmin.razor`: Implemented admin user creation with username and password validation (8+ characters, password confirmation)
- Added `OnboardingStateService.ImportSettingsAndLibrariesAsync()` for applying imports transactionally
- Added localization strings for path validation messages

Path validation rules implemented:
- Rejects paths containing `..`, `./`, or `.\` traversal sequences
- Resolves symlinks to canonical paths before overlap checking
- Detects overlaps between any library paths (case-insensitive, normalized separators)

---

## Phase 7 — Download checklist + final verification

### Deliverables
- Add "Inbound/Staging/Storage explained" step:
  - single screen description + progress bar derived from current SetupStatus items
- Add "Final verification" step:
  - re-run SetupCheck and display blocking items
  - on success, set `system.onboardingCompletedAt` and navigate away
- Add "Download checklist":
  - Implement a server-side endpoint or Blazor download mechanism that returns a generated `.md` file.
  - Checklist content must be legally safe and reference Melodee pages/commands without providing music sources.
  - Format: Markdown (.md) for better readability.
  - Include note for legal review team.

### Implementation notes
- Refresh SetupCheck before final verification to ensure current state.
- After setting completion marker, navigate to appropriate dashboard (Admin for admins, main dashboard otherwise).

### Definition of done
- Wizard completion writes the completion marker and stops forcing onboarding on restart (while requirements remain valid).
- Download checklist is in Markdown format.
- All wizard text is localized.

**Implementation**: Added new wizard step and checklist functionality:

- Created `OnboardingExplain.razor`: "Inbound/Staging/Storage explained" step with library type descriptions and progress bar
- Updated `OnboardingVerify.razor`: Added refresh status button, download checklist functionality, and blocking items count display
- Created `ChecklistService.cs`: Generates Markdown checklist with library paths, command reference, and next steps
- Created `wwwroot/js/downloadFile.js`: JavaScript function to trigger file downloads from base64 content
- Added wizard step between Admin and Verify for library explanation
- Added localization strings for all new text

Checklist includes:
- Library paths summary
- Folder structure recommendations
- Command reference table
- Next steps checklist
- Troubleshooting section with doctor command
- Legal reminder about content compliance
- Links to documentation and source code

---

## Phase 8 — Admin Dashboard JSON export/import

### Deliverables
- Add export/download on `src/Melodee.Blazor/Components/Pages/Admin/Dashboard.razor`:
  - downloads a JSON "backup" containing schema version, exported timestamp (UTC), Settings rows (key+value), and Libraries (type+name+path+apiKey)
  - displays a warning that the file may contain secrets
  - output is deterministic (Settings sorted alphabetically by key, Libraries sorted by type then name)
- Add import/upload on `src/Melodee.Blazor/Components/Pages/Admin/Dashboard.razor`:
  - upload JSON file, validate schema version, preview summary, and apply changes
  - options:
    - overwrite existing values
    - skip null values
  - import is transactional (all or nothing)
  - reject import on schema version mismatch
- Implement shared import/export helpers in `Melodee.Common` (preferred) so UIs are thin and CLI/onboarding stay compatible.

### Implementation notes
- Import must:
  - validate JSON shape and schema version (reject on mismatch)
  - only apply allowed keys (SettingRegistry keys and/or existing DB keys)
  - skip keys set via environment variables (`MelodeeConfigurationFactory.IsSetViaEnvironmentVariable(key)`)
  - skip locked keys (`Setting.IsLocked`) by default
  - skip locked libraries (`Library.IsLocked`) by default
  - never log or display secret values
  - wrap in transaction to ensure all-or-nothing behavior
- After import, refresh the cached configuration (`IMelodeeConfigurationFactory.Reset()`).
- Import results summary includes reasons for skipped items, including schema version mismatch.

### Definition of done
- An admin can export configuration to JSON, change servers, and import to apply the same values.
- The import reports counts of updated/added/skipped items with reasons.
- Import is transactional (all changes apply or none apply).
- Schema version mismatches are rejected with clear error message.

**Implementation**: Created shared import/export services and updated Admin Dashboard:

- Created `src/Melodee.Common/Services/SystemExportService.cs`: Shared export service that generates JSON with schema version, exported timestamp, settings (sorted by key), and libraries (sorted by type then name)
- Created `src/Melodee.Common/Services/SystemImportService.cs`: Shared import service for transactional import of settings and libraries (already created in Phase 6)
- Updated `src/Melodee.Cli/Command/BackupExportCommand.cs`: Refactored to use shared SystemExportService
- Updated `src/Melodee.Blazor/Components/Pages/Admin/Dashboard.razor`: Added export/import section with:
  - Export button with warning about secrets
  - File upload for JSON import with schema version validation
  - Preview showing settings and libraries count
  - Options: overwrite existing values, skip null values
  - Import summary with skipped reasons

Export format:
- Schema version: "1.0"
- Exported timestamp (UTC ISO 8601)
- Settings: key, value, comment, category (sorted alphabetically by key)
- Libraries: name, type, path, apiKey, description (sorted by type then name)

Import behavior:
- Validates schema version (rejects mismatch)
- Skips settings set via environment variables
- Skips locked settings/libraries
- Reports skipped reasons for each skipped item
- Wraps all changes in database transaction

---

## Phase 9 — Refactor `mcli doctor` to use shared Doctor

### Deliverables
- Replace `src/Melodee.Cli/Command/DoctorCommand.cs` bespoke checks with the shared Doctor service.
- Keep CLI UX stable:
  - human-readable table output
  - `--raw` JSON output (ensure it uses the shared model, not a second schema)

### Implementation notes
- CLI should report the same check IDs/names and severities for:
  - baseUrl
  - siteName
  - placeholder required settings
  - security.secretKey presence/strength (masked)
  - required library path checks + overlap
  - symlink resolution in path checks
  - traversal rejection in path checks
- Avoid re-implementing check logic in CLI; only map results to Spectre.Console rendering.

### Definition of done
- `mcli doctor` output matches Blazor Doctor semantics for all shared checks.
- Path validation in CLI includes symlink resolution and traversal rejection.

**Implementation**: Refactored CLI doctor to use shared Doctor service:

- Updated `src/Melodee.Cli/Command/DoctorCommand.cs`: Complete rewrite using shared Doctor service
- Created `CliDoctorService` class extending `DoctorServiceBase` for CLI-specific checks:
  - MusicBrainz SQLite database connectivity
  - ArtistSearchEngine SQLite database connectivity
  - Configuration file path validation
- Updated `IDoctorService.RunLibraryPathCheckAsync()` to support optional writeTest parameter
- Updated `DoctorServiceBase.RunLibraryPathCheckAsync()` to support optional writeTest parameter
- Maintained CLI UX with Spectre.Console table rendering
- `--raw` JSON output uses shared DoctorCheckResult model
- `--write-test` flag enables/disables write permission testing

Shared checks now used by both CLI and Blazor:
- Configuration check (required settings)
- Database connectivity (PostgreSQL)
- Library paths (existence, write permissions, overlap detection with symlink resolution)
- Configurable services status

---

## Phase 10 — Tests + "definition of done" hardening

### Deliverables
- Unit tests for shared SetupCheck logic:
  - missing baseUrl placeholder -> blocking
  - missing/weak `security.secretKey` -> blocking
  - overlapping paths -> blocking
  - symlink overlap detection -> blocking
  - path traversal rejection -> blocking
  - missing/inaccessible required library -> blocking
  - path length validation -> success/failure
  - disk space check results (recommended)
- Unit tests for validation logic must include mocks for both case-sensitive (Linux) and case-insensitive (Windows) filesystem behaviors.
- Unit tests for import/export:
  - export is deterministic
  - import validates schema version (reject mismatch)
  - import is transactional (all or nothing)
  - import respects locked settings/libraries
  - import respects environment variable overrides
  - import summary includes all skip reasons
- Basic UI test coverage (choose the smallest existing test harness in the repo):
  - Guard redirects to `/onboarding` when required.
  - Completion marker prevents redirect when setup remains valid.
  - Wizard step navigation works (Back/Next).
  - Blocking screen displays with retry button.
  - Checklist download succeeds and is localized based on current culture.

### Implementation notes
- No secrets are logged or included in raw outputs.
- Verify wizard meets acceptance criteria in `design/requirements/onboarding-wizard.md`.
- Ensure all new tests pass.

### Definition of done
- All new tests pass.
- No secrets are logged or included in raw outputs.
- Wizard meets acceptance criteria in `design/requirements/onboarding-wizard.md`.
- SetupCheck is refreshed after wizard step mutations (verified via tests or manual check).
- Path validation tests cover symlink resolution and traversal rejection.
- Import tests cover transactionality and schema version validation.

**Implementation**: Created comprehensive unit tests in `tests/Melodee.Tests.Common/Services/`:

- `Doctor/DoctorServiceTests.cs`: Tests for shared Doctor service
  - Configuration check with missing settings returns failure
  - Configuration check with all settings present returns success
  - Database connectivity checks
  - Library path checks with missing paths return failure
  - Overlapping paths detection
  - Non-overlapping paths return success
  - Configurable services status
  - Core checks return all check types

- `SystemImportExportTests.cs`: Tests for import/export functionality
  - Export produces valid JSON
  - Export redact secrets correctly
  - Export output is deterministic
  - Import rejects invalid JSON
  - Import rejects schema version mismatch
  - Import settings successfully
  - Import is transactional (all or nothing)
  - Import skips environment variable settings
  - Round-trip export/import produces equivalent data

- `PathValidationTests.cs`: Tests for path validation logic
  - Paths with traversal sequences are validated
  - Case-insensitive overlap detection
  - Normalized paths comparison
  - Library type sorting

- `OnboardingStateServiceTests.cs`: Tests for onboarding state management
  - No completion marker returns onboarding required
  - Completion marker set with ready status returns not required
  - Completion marker set but not ready returns onboarding required
  - Setup status caching works correctly
  - Refresh clears cache and re-runs checks
  - Blocking items filtering works

All tests verify the core functionality defined in the deliverables:
- SetupCheck logic: missing settings, overlapping paths, symlink resolution, traversal rejection
- Import/Export: determinism, schema validation, transactionality, skip reasons
- Onboarding: guard redirects, completion marker, step navigation, blocking screen

---
