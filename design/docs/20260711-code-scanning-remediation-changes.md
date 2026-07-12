<!-- markdownlint-disable-file -->

# Release Changes: Code Scanning Remediation

**Related Plan**: `20260711-code-scanning-remediation-plan.md`
**Implementation Date**: 2026-07-11

## Summary

This work addresses the 423-alert baseline (416 Trivy and seven CodeQL) through
source-level privacy fixes, setup-secret and filesystem hardening, workflow
supply-chain controls, and production-container dependency remediation. The
verified image inventory is 140 non-fixable medium/low findings. Six original
C# alerts and the fresh C# findings have source fixes; the Python setup alert is
handled by a hardened, necessary persistence boundary and narrow documented
suppression. Fresh Actions and JavaScript/TypeScript scans report zero
findings. Fresh workflow-equivalent default and security-extended Python scans
also report zero findings under the local threat model.

## Changes

### Added

- `design/docs/20260711-code-scanning-remediation-plan.md` - Records the live
  423-alert baseline, remediation phases, and verification criteria.
- `design/docs/20260711-code-scanning-remediation-changes.md` - Tracks the
  security changes and final validation results.
- `.github/codeql/extensions/log-sanitizer/codeql-pack.yml` - Defines an
  auto-discovered local CodeQL model pack.
- `.github/codeql/extensions/log-sanitizer/models/log-sanitizer.model.yml` -
  Models `LogSanitizer.Sanitize` only as a `log-injection` barrier and the
  deny-by-default configuration redactor as a `file-content-store` barrier.
- `.github/codeql/codeql-python-config.yml` - Enables local file, command-line,
  environment, and database sources for Python maintenance scripts without
  changing the compiled application's remote request boundary.
- `src/Melodee.Common/Configuration/ConfigurationLogRedactor.cs` - Adds a
  centralized, deny-by-default policy for configuration values written to
  diagnostic logs.
- `tests/Melodee.Tests.Common/Configuration/ConfigurationLogRedactorTests.cs` -
  Covers sensitive and unknown keys, safe operational values, URL credentials,
  and log-forging characters.
- `src/Melodee.Blazor/Services/IPasswordResetTokenGenerator.cs` - Adds a narrow
  reset-token generation abstraction so the complete component flow can be
  regression-tested without the full user-service facade.
- `tests/Melodee.Tests.Blazor/Components/Pages/ForgotPasswordTests.cs` - Covers
  reset success, unknown accounts, delivery failure, rate limiting, exceptions,
  unsafe base URLs, and absence of email/token/configuration values in logs.
- `scripts/private_config.py` - Centralizes atomic secret-file publication,
  explicit overwrite revalidation, POSIX mode enforcement, symlink and
  non-regular-file refusal, and partial-file cleanup.
- `scripts/tests/test_private_config.py` - Covers shared writer publication,
  cleanup, overwrite, and destination-integrity behavior.
- `scripts/tests/test_run_container_setup.py` - Covers generated-secret privacy,
  explicit overwrite, POSIX permissions, symlink refusal, and non-regular
  destination handling for the interactive container setup path.
- `scripts/tests/test_code_scanning_security.py` - Covers bounded Link-header
  parsing under adversarial input, exact-origin HTTPS request and redirect
  enforcement, bounded redirect behavior, and secret-free demo-user
  success/failure output.
- `scripts/tests/test_incoming_clean_up.py` - Covers canonical root boundaries,
  symlink and outside-root refusal, Zip Slip/symlink-member rejection, SFV
  traversal, private extraction permissions, descriptor-relative race defense,
  safe encoding repair, pretend mode, and contained live deletion.
- `tests/Melodee.Tests.Common/Services/SecurityLogSanitizationTests.cs` - Covers
  the untrusted player, playlist, script setting, configuration key, provider,
  and artist values reported by the fresh C# CodeQL scan.
- `tests/Melodee.Tests.Common/TestHelpers/RecordingLogEventSink.cs` - Captures
  rendered security-sensitive log events for assertions without external
  logging infrastructure.

### Modified

- `src/Melodee.Blazor/Components/Pages/Account/ForgotPassword.razor` - Removed
  private values from all reset logs, validates a credential-free absolute
  HTTP(S) base URL, and preserves generic diagnostics and enumeration-resistant
  reset behavior.
- `src/Melodee.Blazor/Program.cs` - Registers the reset-token abstraction.
- `src/Melodee.Blazor/Services/Email/SmtpEmailSender.cs` - Removes subjects,
  configured hosts, exception objects, and exception messages from SMTP logs.
- `src/Melodee.Common/Services/UserAuthenticationService.cs` - Replaces
  usernames/emails with internal IDs in authentication and migration logs and
  stops propagating the unused sensitive login identifier.
- `src/Melodee.Common/Services/UserProfileService.cs` - Removes email and
  username values from lookup timing logs.
- `src/Melodee.Blazor/Filters/MelodeeApiAuthFilter.cs` - Replaces blacklisted
  user email/IP logging with the internal user ID.
- `tests/Melodee.Tests.Blazor/Services/EmailServiceTests.cs`,
  `tests/Melodee.Tests.Blazor/Controllers/Melodee/MelodeeApiAuthFilterTests.cs`,
  and `tests/Melodee.Tests.Common/Services/UserAuthenticationServiceTests.cs` -
  Add focused secret/identifier-free logging regression coverage.
- `src/Melodee.Common/Configuration/MelodeeConfigurationFactory.cs` - Routes
  startup environment diagnostics through the centralized redactor so raw
  passwords, tokens, connection strings, and unknown values never reach
  `Trace` output.
- `scripts/setup_melodee.py` - Generates database and authentication secrets
  without logging them and delegates ignored `.env` publication to the shared
  secure writer.
- `scripts/run-container-setup.py` - Uses the same secure writer for normal,
  confirmed-overwrite, and forced setup paths.
- `scripts/create_code_scanning_combined_serif.py` - Replaces a polynomial
  backtracking Link-header expression with a bounded linear parser and confines
  API requests, pagination, redirects, and downloads to the configured exact
  HTTPS origin.
- `scripts/create-demo-user.py` - Stops printing the demo password, generated
  API/public keys, encrypted password, and secret-bearing exception payloads.
- `scripts/incoming_clean_up.py` - Adds an explicit trusted filesystem boundary,
  canonical containment before every access/mutation, pinned descriptor-relative
  no-follow operations, `renameat2(RENAME_NOREPLACE)` quarantine publication,
  full ZIP-member preflight/extraction, and traversal-safe SFV verification and
  renaming. Live cleanup fails closed where the required Linux/POSIX primitives
  are unavailable; extracted directories and files are private `0700` and
  `0600` objects respectively.
- `scripts/tests/test_setup_melodee.py` - Covers secret generation, POSIX
  permissions, existing-file preservation, malformed templates, symlink
  refusal, and non-regular destinations.
- `design/docs/codeql-fixes.md` - Replaces the stale false-positive guidance
  with the current CodeQL baseline, source remediations, precise barrier model,
  workflow coverage, verification, and server-side cleanup instructions.
- `.github/workflows/codeql.yml` - Adds GitHub Actions and Python to advanced
  scanning, declares language-specific build modes, pins actions by commit,
  limits token permissions, and supports manual runs.
- `.github/codeql/codeql-config.yml` - Removes invalid model configuration and
  broad rule exclusions while retaining the default remote threat model for
  the compiled web application.
- `src/Melodee.Common/Services/DeviceIdentificationService.cs`,
  `src/Melodee.Common/Services/PlaylistImportService.cs`,
  `src/Melodee.Common/Services/ScriptEvaluation/ScriptAdminService.cs`,
  `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs`, and
  `src/Melodee.Common/Services/SettingService.cs` - Sanitize the six
  attacker-controlled fields found by the fresh default-remote C# log-forging
  scan.
- `src/Melodee.Common/Services/Playback/Backends/MpdPlaybackBackend.cs` - Keeps
  the MPD password command on the wire while passing only a credential-free
  command representation to every log path.
- `tests/Melodee.Tests.Common/Services/Playback/PlaybackBackendTests.cs` -
  Verifies that password authentication still reaches MPD and never reaches
  application logs.
- `.github/CODEQL-WORKFLOW.md` - Documents the single advanced setup, model
  behavior, verification, and stale GitHub configuration cleanup.
- `Dockerfile` - Moves the shipped runtime to the official .NET 10 Ubuntu 26.04
  image, applies current package upgrades, replaces vulnerable unused
  coreutils/Pebble tooling, asserts package integrity and required commands,
  and defaults to the unprivileged `melodee` account.
- `entrypoint.sh` - Handles all persistent-volume paths and replaces the root
  repair process with `dotnet` as UID/GID 999 and PID 1 through `setpriv`.
- `.github/workflows/sca-container-scan.yml` - Builds and scans the real
  production image on native amd64 and arm64 runners, corrects SARIF severity
  filtering, pins every action, and retains complete all-severity JSON reports.
- `.github/workflows/docker-publish.yml` - Forces the security-sensitive final
  apt layer to refresh, pins every external action, validates image digests,
  and constructs multi-architecture manifest arguments without shell word
  splitting.
- `.github/workflows/dotnet.yml` - Pins every external action and writes the
  coverage summary with quoted, grouped shell redirection.
- `.github/workflows/gitleaks.yml` - Pins checkout and SARIF upload actions by
  commit and the Gitleaks container action by immutable multi-platform digest.
- `.github/workflows/localization.yml` - Pins every external action and hardens
  shell quoting and summary output without changing localization behavior.
- `docs/pages/changelog.md` - Records the source, workflow, runtime-image, and
  vulnerability-reporting security changes for 2.2.0.
- `docs/_posts/2026-07-11-melodee-2-2-0-released.md` - Adds the release-level
  password-reset, setup-secret, non-root runtime, CodeQL, and Trivy summary.

### Removed

- `.github/codeql/extensions/log-sanitizer.model.yaml` - Replaced the
  undiscovered value-propagating summary with the auto-discovered precise model
  pack.
- `.github/docker/Dockerfile.container-scan` - Removed the reduced scan image
  so CI analyzes the same final image that releases publish.

## Verified Results

### CodeQL

- The six live baseline C# password-reset privacy flows have source fixes. The
  Python setup-secret alert is handled by atomic owner-only persistence,
  regression coverage, and one narrow documented suppression for the necessary
  `.env` sink.
- A fresh C# database using the default remote threat model moved from eight
  findings to zero after fixing six log-forging paths and two MPD
  password-to-log flows.
- Fresh GitHub Actions and JavaScript/TypeScript databases each report zero
  findings.
- Restored local Python analysis identified the setup persistence boundary,
  polynomial Link parsing, secret-bearing output, filesystem/ZIP/SFV paths, and
  same-origin exporter request handling. A fresh workflow-equivalent database
  reports zero findings and no SARIF warning/error notifications across all 45
  default queries. The 52-query security-extended suite also reports zero
  findings.

The shared configuration keeps the compiled C# application, JavaScript, and
Actions on CodeQL's default remote trust boundary. Python alone enables the
local threat model because maintenance scripts intentionally consume command
line arguments, environment variables, and local files. This avoids treating
ordinary application-owned database and filesystem state as remotely
attacker-controlled while preserving the stricter model where it represents
the Python tools' real inputs.

### Container and Dependency Inventory

- Trivy 0.72.0 reduced raw production-image findings from 416 to 140: 276
  removed (66.3%). The remaining inventory represents 41 CVEs, split into 124
  medium and 16 low findings.
- The remaining image inventory contains zero critical, high, fixable,
  .NET-package, or application-package findings.
- GitHub receives only the intended critical/high SARIF policy. CI retains the
  complete all-severity JSON report for review and future fix-availability
  changes.
- NuGet reports zero vulnerable dependencies.

### Build and Runtime Verification

- The complete solution builds with zero warnings and errors.
- The full .NET suite passes 5,885 tests with 34 skipped and zero failed.
- All 109 Python script tests pass with `ResourceWarning` promoted to an error,
  including 57 focused incoming-cleanup security and race regressions.
- Jekyll, `actionlint`, YAML parsing, shell syntax/static analysis, and Docker
  configuration checks pass.
- A real PostgreSQL integration starts the production image with the
  unprivileged `melodee` process as PID 1, reaches healthy status, and confirms
  that configured database/authentication secrets are absent from container
  logs.

## Release Summary

The local release gate is complete: all four CodeQL languages report zero fresh
findings, the production image contains no critical, high, or currently fixable
inventory, the .NET and Python suites pass, and the production container passes
its real-database non-root integration. The remaining 140 raw image findings
are quantified medium/low Ubuntu-package issues without available fixes.

## Post-Merge Verification

- The final pre-merge API query still reports the 423-alert `main` baseline.
  This is expected because GitHub has not analyzed this branch; no server-side
  alert closure is claimed by the local results.
- After the checked-in Python job completes successfully on `main`, delete the
  stale `dynamic/github-code-scanning/codeql:analyze` and
  `dynamic/github-code-scanning/codeql:upload` Python configurations from
  **Security and quality > Code scanning > Tool status**. Retain
  `.github/workflows/codeql.yml:analyze`.
