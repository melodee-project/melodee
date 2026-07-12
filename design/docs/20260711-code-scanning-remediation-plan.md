<!-- markdownlint-disable-file -->

# Code Scanning Remediation Plan

**Implementation Date**: 2026-07-11

## Objective

Reduce the 423 open GitHub code-scanning alerts on `main` with verified,
behavior-preserving fixes. Prioritize the seven CodeQL findings and remove as
many of the 416 Trivy container findings as possible without hiding actionable
risk or removing required Melodee runtime features.

## Baseline

- Total: 423 open code-scanning alerts on `main`.
- CodeQL: seven alerts.
  - Six `cs/exposure-of-sensitive-information` findings in the forgot-password
    page.
  - One `py/clear-text-storage-sensitive-data` finding in the setup script.
- Trivy: 416 alerts in the `library/melodee` container image.
  - The baseline contained 382 medium and 34 low findings, with no critical or
    high findings.
  - Most findings come from the Ubuntu FFmpeg 6.1 package and its shared
    libraries.

## Phase 1: Inventory and Triage

- [x] Retrieve every open alert through the GitHub code-scanning API.
- [x] Group alerts by tool, rule, severity, path, package, and installed
  version.
- [x] Split independent CodeQL and container work into parallel workstreams.

## Phase 2: CodeQL Remediation

- [x] Remove sensitive-information exposure from the forgot-password flow
  while retaining enumeration resistance and useful diagnostics.
- [x] Harden the Python setup flow's necessary `.env` persistence with atomic,
  owner-only writes and document the narrow unavoidable CodeQL sink.
- [x] Add or update focused regression tests for both flows.
- [x] Consolidate GitHub Actions, C#, JavaScript/TypeScript, and Python under
  the advanced CodeQL workflow and replace broad query exclusions with precise
  modeling.
- [x] Redact database passwords, authentication keys, tokens, and connection
  strings from startup configuration logs.
- [x] Remove direct email/user identifiers, reset configuration values, and
  exception payloads from adjacent authentication and SMTP logs.
- [x] Remediate the ReDoS, demo-secret logging, and filesystem path-injection
  findings exposed by the newly enabled local Python CodeQL scan.
- [x] Restrict the GitHub code-scanning exporter to bounded, same-origin HTTPS
  requests and redirects so pagination cannot become an SSRF path.
- [x] Reduce a fresh default-remote C# scan from eight findings to zero by
  sanitizing six log-forging flows and separating MPD wire commands from their
  credential-free log representation.

## Phase 3: Container Remediation

- [x] Refresh or replace vulnerable operating-system and media packages using
  trusted upstream images and repositories.
- [x] Keep FFmpeg, PostgreSQL client tools, health checks, non-root execution,
  and supported architectures functional.
- [x] Align the production Dockerfile and the CI scan image so the scan
  represents the shipped runtime.
- [x] Generate a local post-change Trivy inventory and quantify remaining
  findings.

## Phase 4: Verification and Release Notes (Complete)

- [x] Run focused tests for the completed C#, setup, workflow, and container
  fixes.
- [x] Build the production container and smoke-test required executables and
  non-root startup behavior.
- [x] Run the full .NET build and test suite without warnings.
- [x] Complete the Python filesystem audit, its final focused tests, and
  a fresh full-query Python CodeQL scan with the local threat model.
- [x] Validate Jekyll, GitHub Actions, YAML, shell, and Docker configuration
  changes.
- [x] Update the public changelog and release change record with the final
  verified results.

## Verification Snapshot

- Six original C# CodeQL alerts have source fixes and regression coverage. The
  Python setup alert is handled by a hardened, necessary persistence boundary,
  regression coverage, and one narrow documented suppression.
- Fresh local CodeQL scans report C# at `8 -> 0`, GitHub Actions at zero, and
  JavaScript/TypeScript at zero under the default remote threat model.
- A fresh workflow-equivalent local-threat-model Python database reports zero
  findings and no SARIF warning/error notifications across all 45 default
  queries. The 52-query security-extended suite also reports zero findings.
- The Python test run passes all 109 script tests with `ResourceWarning`
  promoted to an error, including all 57 focused cleanup filesystem, ZIP, SFV,
  shutdown, and race regressions.
- Trivy 0.72.0 reduced the production-image inventory from 416 to 140 raw
  findings: 276 removed (66.3%), 41 remaining CVEs, 124 medium and 16 low.
  None are critical, high, fixable, or attributable to .NET/application
  packages.
- The solution builds with zero warnings. The full .NET suite passes 5,885
  tests with 34 skipped, and NuGet reports zero vulnerable dependencies.
- Jekyll, `actionlint`, YAML, shell, and container checks pass. A real
  PostgreSQL container integration confirmed health, non-root PID 1 startup,
  and that configured secret values do not appear in logs.

## Success Criteria

- All seven baseline CodeQL data-flow findings have either a source fix or a
  narrowly documented, security-hardened necessary persistence boundary, with
  regression coverage.
- The container alert count is materially reduced without suppressing fixed or
  high-impact vulnerabilities.
- The solution and container continue to build and required tests pass.
- Remaining alerts are quantified by severity, package, and fix availability.
- The Python audit and local-threat-model scans are complete; GitHub alert
  reconciliation remains a post-merge administrative step.
