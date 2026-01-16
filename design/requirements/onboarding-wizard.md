# Onboarding Wizard Requirements

## Problem statement

Melodee’s first-run experience is currently “discoverable” rather than “guided”. New administrators must infer required
configuration and filesystem setup from errors, documentation, and the Doctor page. This increases stress and increases
time-to-first-stream.

## Goals

- Make first-run setup unmissable and low-stress.
- Provide a single onboarding wizard that guides a new admin to a “ready to ingest and stream” system state.
- Reuse and standardize Doctor checks across `Melodee.Blazor` and `Melodee.Cli` so “Doctor” is uniform across the
  solution.
- Ensure that missing critical configuration results in a deterministic, recoverable UX (wizard shown; user can fix).

## Non-goals

- Provide sample music or any content that implies distribution rights.
- Auto-download copyrighted music, provide “example” sources, or enable any legally ambiguous workflows.
- Replace the existing Admin Settings / Libraries pages; the wizard may re-use their services and components.

## Definitions

- **Doctor**: The system-wide health checking mechanism and its set of checks, results, and severities.
- **SetupCheck**: A doctor check group that answers: “Is the instance configured enough to run safely and ingest media?”
- **DoctorWizard**: A lightweight startup orchestrator that calls `SetupCheck()` on each app start and decides whether the
  onboarding wizard must be shown.
- **Blocking (Critical) issue**: A failing requirement that prevents setup completion and triggers onboarding.
- **Recommended issue**: A failing check that is surfaced but does not block setup completion.
- **Onboarding required**: A runtime condition that forces navigation to the onboarding wizard.

## Entry conditions (when onboarding is shown)

On each application start, the Blazor host (via `DoctorWizard`) performs `SetupCheck()` and determines
`OnboardingRequired`.

The onboarding wizard is shown when **either** is true:

1. **Setup is not complete**: `system.onboardingCompletedAt` is missing/empty.
2. **Critical setup is failing**: `SetupCheck().IsReady == false`.

Notes:
- If onboarding was previously completed but later a critical setup requirement becomes invalid (e.g., paths removed,
  required settings cleared), onboarding must be shown again so the admin can fix it.
- If the system cannot reach the DB/configuration required to compute `SetupCheck`, show a dedicated blocking screen
  that explains the failure and how to resolve it (no “blank page” or redirect loops).

## Required setup checks (blocking)

### Branding (Settings table)

1) `system.baseUrl` must be configured:
- Source: `Settings` table key `system.baseUrl` (`SettingRegistry.SystemBaseUrl`).
- Invalid values:
  - missing setting row (key not present)
  - `null`/empty/whitespace
  - `MelodeeConfiguration.RequiredNotSetValue`
  - not a valid absolute `http` or `https` URL
- Validation rules:
  - Must parse as `Uri` absolute.
  - Must be `http` or `https`.
  - Must not end with whitespace; value is stored trimmed.
  - Wizard shows a preview of derived URLs used by the app (e.g., image URL shape) but must not require external calls.

2) `system.siteName` must be configured:
- Source: `Settings` table key `system.siteName` (`SettingRegistry.SystemSiteName`).
- Invalid values:
  - missing row
  - `null`/empty/whitespace
- Default seed value `"Melodee"` is acceptable, but the wizard must explicitly present this step and let the admin confirm
  or change it (to make branding “unmissable”).

### Critical configuration (Settings table)

3) All required settings must not be set to the placeholder:
- Any `Settings.Value` equal to `MelodeeConfiguration.RequiredNotSetValue` is considered required and blocking.
- The onboarding wizard must list these keys and provide UI to set them.

4) `security.secretKey` must exist and be strong:
- Source: `Settings` table key `security.secretKey` (`SettingRegistry.SecuritySecretKey`).
- Invalid values:
  - missing row
  - `null`/empty/whitespace
  - length < 32 characters
- Wizard behavior:
  - Provide a “Generate secure key” action that generates a cryptographically secure value and stores it in the DB.
  - Provide a “Regenerate” action with explicit confirmation and a warning about invalidating existing protected data.
  - Never display the full key after it has been saved; show masked value and “copy new key” only at creation time.

### Library paths (filesystem + Libraries table)

5) Required libraries must exist and be usable:
- Source: `Libraries` table (via `LibraryService`).
- Required library types for onboarding completion:
  - `LibraryType.Inbound`
  - `LibraryType.Staging`
  - At least one `LibraryType.Storage`
- Each required library must pass:
  - The directory exists OR the wizard can create it (admin choice).
  - The directory is writable by the running process (wizard uses a safe write test file).

6) Inbound/Staging/Storage must not overlap:
- Paths must not be equal and must not be parent/child of each other (case-insensitive, normalized).
- Overlap is blocking because it can cause destructive moves or confusing processing behavior.

## Recommended setup checks (non-blocking)

These should be visible in onboarding (as warnings) but must not prevent completing onboarding:

- MusicBrainz DB file exists and is initialized (this can be heavy; do not block first-run).
- HTTPS enabled in production (block only if product direction changes later).
- FFmpeg available (block only if conversion is required for the chosen workload).
- SMTP configured when email is enabled (only block when email is enabled and password reset is expected to work).
- Jukebox/Podcast configuration when those features are enabled.

## Wizard UX requirements

### Wizard layout

- Route: `/onboarding` (exact path).
- Always shows:
  - stepper/progress indicator (X of Y)
  - “Back”/“Next” controls
  - clear “Blocking” vs “Recommended” labeling
- Navigation:
  - When `OnboardingRequired == true`, users must not be able to navigate to other pages (hard redirect/guard).
  - The only allowed routes while onboarding is required:
    - `/onboarding` and its child routes
    - `/account/login` (if authentication is required before onboarding)
    - `/account/logout`
    - a dedicated `/onboarding/blocking` page for “cannot compute setup status” failures (DB unreachable, etc.)

### Authentication/authorization

- Onboarding must be actionable only by admins (users with Admin role/claim).
- If no admin exists in the database:
  - onboarding must provide a “Create first admin” step, or
  - onboarding must provide a blocking screen instructing how to create an admin via CLI (if implemented later).
- All mutations performed by the wizard must use existing domain services (`SettingService`, `LibraryService`, etc.) and
  respect authorization checks already present in those services/pages.

### Step requirements (minimum set)

1) **Welcome / context**
- Explain what the wizard will do and that it can be re-entered if critical checks fail later.
- Show a summary of current blocking items detected by `SetupCheck()`.
- Provide an optional “Import existing system export” action that allows uploading the JSON system export (created by the
  UI or `mcli`) to pre-fill Settings + Libraries. If import succeeds, the wizard should re-run `SetupCheck()` and skip
  completed steps.

2) **Branding**
- Inputs:
  - `system.siteName` (text)
  - `system.baseUrl` (URL)
- Validations per “Required setup checks”.
- Copy should mention where these values are used (emails, shareable links, image URLs).

3) **Security key**
- Detect whether `security.secretKey` is configured.
- Provide “Generate key” with local generation.
- Show warnings about regeneration.

4) **Library paths (wizard + defaults)**
- Show the required libraries and their current configured paths.
- Provide recommended defaults based on runtime environment:
  - If running in container with default mount patterns: `/app/inbound`, `/app/staging`, `/app/storage`
  - Otherwise: suggest platform-appropriate defaults (agents will implement OS detection)
- Provide actions:
  - Browse/select path (if supported) or manual entry
  - Create directory
  - Test write permissions
- Block Next until required libraries pass (exists + writable + no overlap).

5) **Inbound/Staging/Storage explained (single screen + progress bar)**
- Single informational step describing the lifecycle:
  - Inbound: “drop new media here”
  - Staging: “processed/normalized metadata”
  - Storage: “final library for streaming”
- Show a progress bar that reflects “setup readiness for ingestion”, derived from:
  - configured paths
  - writeability
  - overlap check
  - (optional) whether scheduled jobs are enabled

6) **Final verification**
- Re-run `SetupCheck()` and show a compact success/failure list.
- “Complete setup” button is enabled only when all blocking checks pass.
- On completion:
  - set `system.onboardingCompletedAt` in `Settings` with UTC timestamp (`Instant` serialized consistently with existing
    settings conventions).
  - navigate to Admin dashboard (or main dashboard depending on role).

7) **Download next-steps checklist**
- Provide a “Download checklist” action that downloads a text/markdown file generated server-side.
- Content requirements:
  - A clear, legally-safe statement: “Add only media you own/are licensed to use.”
  - Steps to add music without providing sources:
    - choose a folder structure
    - copy/rip/purchase/import your own files into Inbound
    - run processing / scanning
    - verify in UI
    - optionally configure podcasts (RSS you control/subscribe to)
  - References to relevant Melodee pages (e.g., Libraries page, Admin Settings, Jobs).

## Doctor standardization requirements

### Single doctor implementation across Blazor + CLI

- The check implementations and their definitions (name/id, severity, remediation hints) must live in a shared project
  referenced by both `Melodee.Blazor` and `Melodee.Cli` (target location: `src/Melodee.Common`).
- `Melodee.Cli doctor` must use the same Doctor service/check engine as the Blazor host.
- The Blazor Doctor page may add host-only checks (HTTPS detection via `HttpContext`, Serilog sinks, scheduler) but must
  do so through the same shared model and result types.

### SetupCheck API contract (shared)

`SetupCheck()` (and any supporting method like `GetSetupStatus()`) must return enough information for:
- startup gating (`IsReady`)
- the wizard step list (which items are missing)
- per-item remediation hints (what to do / which UI step can fix it)

At minimum, each setup item must include:
- `Id` (stable identifier)
- `Name`
- `Severity` (`Blocking` or `Recommended`)
- `Success`
- `Details` (safe to show in UI; never include secrets)
- `Remediation` (short guidance)

## System export/import requirements (JSON “backup”)

Provide an export/import experience on `/admin/dashboard` to help:

- quickly test configuration changes,
- migrate configuration between servers/environments.

This is an export/import of **configuration data** (at minimum: Settings + Libraries), not a database backup.

### Export (download)

- The `/admin/dashboard` page must provide an action to download a JSON “backup” of configuration.
- Export content includes:
  - schema version
  - exported timestamp (UTC)
  - all Settings rows (`key` + `value` at minimum)
  - all Libraries rows needed to recreate library setup (at minimum: `type`, `name`, `path`, and a stable identifier such as `apiKey`)
- Export must clearly warn that the file may contain secrets and should be stored securely.

### Import (upload)

- The `/admin/dashboard` page must provide an action to upload a previously exported JSON “backup”.
- Import must:
  - validate schema version and JSON shape
  - only apply allowed keys (known SettingRegistry keys and/or existing keys in the DB)
  - skip settings that are set via environment variables on the current host (cannot be overridden via DB)
  - respect `Setting.IsLocked` by default (skip locked keys and report)
  - provide an “overwrite existing values” option
  - provide a “skip null values” option (null values are otherwise treated as empty string)
- Import must also support Libraries:
  - match existing libraries by stable identifier (`apiKey`), else fall back to `type` for unique types
  - respect `Library.IsLocked` by default (skip locked libraries and report)
  - provide an “overwrite existing values” option controlling whether paths/names are updated
- Import must show a results summary:
  - updated count
  - added count
  - skipped count (with reasons: unknown key, env override, locked, null-skipped, validation failure)
- Import must never log or display secret values; logs may include counts and key names only if safe.

### CLI export (commandline)

- Melodee.Cli must provide a command to create the same system “export” from the command line.
- The CLI-generated export must be **byte-for-byte compatible in schema** with the UI/onboarding import (same schema
  version, same JSON shape, same field meanings).
- The CLI export is intended for snapshots and migration; it must support writing to a file and/or stdout.
- The CLI export must include Settings + Libraries (minimum) and any other configuration surfaced in the UI export.
- The CLI must warn that exports may include secrets; it must provide an explicit option to redact secrets (default can
  be either, but must be explicit and documented).

## Acceptance criteria

- A brand-new instance launches to `/onboarding` (unmissable) until setup completion.
- If any blocking requirement becomes invalid, `/onboarding` is forced again on next start.
- The wizard can successfully set:
  - `system.baseUrl`
  - `system.siteName`
  - `security.secretKey` (generated)
  - Inbound/Staging/Storage library paths (validated and writable)
  - `system.onboardingCompletedAt`
- The Blazor Doctor page and `mcli doctor` share the same checks for all items listed under “Required setup checks”.
- No secrets are logged or rendered in raw output; sensitive values are masked in all views.
- `/admin/dashboard` supports downloading and uploading the JSON “backup” to export/import system configuration.
- `mcli` can generate the same JSON export for snapshots and migration.
