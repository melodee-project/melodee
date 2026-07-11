<!-- markdownlint-disable-file -->

# Code Scanning Remediation Plan

**Implementation Date**: 2026-07-11

## Objective

Reduce the 423 open GitHub code-scanning alerts on `main` with verified,
behavior-preserving fixes. Prioritize the seven CodeQL findings and remove as
many of the 416 Trivy container findings as possible without hiding actionable
risk or removing required Melodee runtime features.

## Baseline

- CodeQL: seven alerts
  - Six `cs/exposure-of-sensitive-information` findings in the forgot-password
    page.
  - One `py/clear-text-storage-sensitive-data` finding in the setup script.
- Trivy: 416 alerts in the `library/melodee` container image.
  - All current Trivy findings are medium or low severity.
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
- [x] Remove clear-text password persistence from the Python setup flow while
  preserving interactive and unattended setup behavior.
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

## Phase 3: Container Remediation

- [x] Refresh or replace vulnerable operating-system and media packages using
  trusted upstream images and repositories.
- [x] Keep FFmpeg, PostgreSQL client tools, health checks, non-root execution,
  and supported architectures functional.
- [x] Align the production Dockerfile and the CI scan image so the scan
  represents the shipped runtime.
- [x] Generate a local post-change Trivy inventory and quantify remaining
  findings.

## Phase 4: Verification and Release Notes

- [ ] Run focused tests for each security fix.
- [ ] Build the production container and smoke-test required executables and
  non-root startup behavior.
- [ ] Run the full .NET build and test suite without warnings.
- [ ] Validate Python, YAML, shell, and Docker configuration changes.
- [ ] Update the public changelog and complete the release change record.

## Success Criteria

- All seven current CodeQL data-flow findings have source fixes and regression
  coverage or a documented, evidence-backed reason they remain.
- The container alert count is materially reduced without suppressing fixed or
  high-impact vulnerabilities.
- The solution and container continue to build and required tests pass.
- Remaining alerts are quantified by severity, package, and fix availability.
