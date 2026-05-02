# Melodee Version Guide

This document outlines how application versioning works in Melodee and the steps required to bump versions following [Semantic Versioning](https://semver.org/) (SemVer).

## Semantic Versioning Overview

Melodee uses the `MAJOR.MINOR.PATCH` format:

| Segment | When to Increment | Example |
|---------|-------------------|---------|
| **MAJOR** | Incompatible API changes, breaking UI/behavior changes, or major architectural shifts | `1.x.x` → `2.0.0` |
| **MINOR** | New backward-compatible features, new endpoints, new UI pages | `1.2.x` → `1.3.0` |
| **PATCH** | Bug fixes, security patches, performance improvements, documentation | `1.2.3` → `1.2.4` |

### Decision Guide

**Increment MAJOR when:**
- Removing or changing existing API endpoints in a breaking way
- Changing database schemas in a non-migratable way
- Removing or renaming configuration settings without backward compatibility
- Dropping support for existing client integrations (OpenSubsonic, Jellyfin-compatible API)
- Major UI overhaul that changes established user workflows

**Increment MINOR when:**
- Adding new API endpoints (non-breaking)
- Adding new UI features or pages (e.g., Party Mode, Jukebox, Podcasts)
- Adding new configuration settings with sensible defaults
- Adding new plugin types or extensibility points
- Adding new localization support

**Increment PATCH when:**
- Fixing bugs in existing functionality
- Updating dependencies for security patches
- Performance optimizations that don't change behavior
- UI/UX polish (styling, accessibility improvements)
- Documentation updates
- CI/CD pipeline fixes

## Current Versioning Architecture

Melodee has **two independent version tracks** that must be kept in sync during releases:

### Track 1: Assembly Version (.csproj files)

Used at runtime, displayed in the About page, and embedded in compiled binaries.

**Files (all 4 must be updated together):**

| File | Property | Current Value |
|------|----------|---------------|
| `src/Melodee.Blazor/Melodee.Blazor.csproj` | `VersionPrefix` | `2.0.0` |
| `src/Melodee.Common/Melodee.Common.csproj` | `VersionPrefix` | `2.0.0` |
| `src/Melodee.Cli/Melodee.Cli.csproj` | `VersionPrefix` | `2.0.0` |
| `src/Melodee.Mql/Melodee.Mql.csproj` | `VersionPrefix` | `2.0.0` |

Each .csproj also defines:
- `VersionSuffix` — auto-generated build timestamp (e.g., `build20260501165851`)
- `AssemblyVersion` — `$(VersionPrefix).0` (e.g., `2.0.0.0`)
- `FileVersion` — `$(VersionPrefix).0` (e.g., `2.0.0.0`)
- `InformationalVersion` — `$(VersionPrefix)+$(VersionSuffix)` (e.g., `2.0.0+build20260501165851`)

The `AppVersionProvider` service strips the suffix and displays only the `VersionPrefix` (e.g., `2.0.0`) in the UI.

### Track 2: Docker Image Tags (GitHub Releases)

Docker images are published to `ghcr.io` and tagged based on **GitHub release tags**.

**Workflow:** `.github/workflows/docker-publish.yml`

**Trigger:** GitHub release published (or manual `workflow_dispatch`)

**Tag patterns:**
- `{{version}}` — full SemVer (e.g., `2.0.0`)
- `{{major}}.{{minor}}` — minor track (e.g., `2.0`)
- `{{major}}` — major track (e.g., `2`)
- `latest` — applied to every release

### Track 3: docs/VERSION (Orphaned)

The file `docs/VERSION` currently contains `0.0.31` and is **not referenced by any code, build, or CI pipeline**. It appears to be a legacy artifact. Consider removing it or integrating it into the release process.

## Step-by-Step Version Bump Procedure

### Prerequisites

- All changes for the release are merged to `main`
- CI pipeline (`.github/workflows/dotnet.yml`) passes on `main`
- You have write access to the repository

### Step 1: Update the Changelog

Edit `docs/pages/changelog.md` and:

1. Replace the `[Unreleased]` section header with the new version and today's date:
   ```markdown
   ## [X.Y.Z] - YYYY-MM-DD
   ```

2. Add a fresh `[Unreleased]` section at the top for future entries:
   ```markdown
   ## [Unreleased]
   ```

3. Categorize all changes since the last release using [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) types:

   | Type | Use For |
   |------|---------|
   | **Added** | New features, endpoints, UI pages, configuration options |
   | **Changed** | Modifications to existing functionality or behavior |
   | **Deprecated** | Features that will be removed in a future release |
   | **Removed** | Features removed in this release |
   | **Fixed** | Bug fixes and error corrections |
   | **Security** | Vulnerability patches and security hardening |

4. Source changes from:
   - Git commit history since the last tag (`git log vX.Y.Z..HEAD --oneline`)
   - Merged PRs and their labels
   - The GitHub release draft notes

### Step 2: Update Assembly Versions

Edit the `VersionPrefix` in all four .csproj files:

```
src/Melodee.Blazor/Melodee.Blazor.csproj
src/Melodee.Common/Melodee.Common.csproj
src/Melodee.Cli/Melodee.Cli.csproj
src/Melodee.Mql/Melodee.Mql.csproj
```

Change `<VersionPrefix>X.Y.Z</VersionPrefix>` to the new version.

> **Future improvement:** Centralize this in `Directory.Build.props` so all projects inherit a single version definition:
> ```xml
> <Project>
>   <PropertyGroup>
>     <MelodeeVersion>2.1.0</MelodeeVersion>
>     <VersionPrefix>$(MelodeeVersion)</VersionPrefix>
>     <VersionSuffix>build$([System.DateTime]::UtcNow.ToString("yyyyMMddHHmmss"))</VersionSuffix>
>     <AssemblyVersion>$(VersionPrefix).0</AssemblyVersion>
>     <FileVersion>$(VersionPrefix).0</FileVersion>
>     <InformationalVersion>$(VersionPrefix)+$(VersionSuffix)</InformationalVersion>
>   </PropertyGroup>
> </Project>
> ```

### Step 3: Commit and Open a PR

```bash
git add docs/pages/changelog.md src/*/ docs/VERSION 2>/dev/null || true
git commit -m "chore: release vX.Y.Z"
git push origin <branch>
```

Open a PR targeting `main`. The version bump is reviewed like any other change.

### Step 4: Merge and Tag

After the PR is approved and merged to `main`:

```bash
git checkout main && git pull
git tag -a vX.Y.Z -m "Release vX.Y.Z"
git push origin vX.Y.Z
```

> The tag **must** be created on `main` after merge so the `docker-publish.yml` workflow picks it up.

### Step 5: Create a GitHub Release

1. Go to **GitHub → Releases → Draft a new release**
2. Select the tag `vX.Y.Z`
3. Title: `vX.Y.Z`
4. Description: Copy the changelog entries for this version from `docs/pages/changelog.md` (everything under the `## [X.Y.Z]` header down to the next `##` heading). Use the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) section headings:

   ```markdown
   ### Added
   - New feature description

   ### Changed
   - Modified behavior description

   ### Fixed
   - Bug fix description

   ### Security
   - Security improvement description
   ```

5. Click **Publish release**

### Step 6: Verify Docker Image Publication

After publishing the release, the `docker-publish.yml` workflow triggers automatically. Verify:

1. Check the **Actions** tab for the `Docker Publish` workflow run
2. Confirm all platforms build successfully (`linux/amd64`, `linux/arm64`)
3. Verify the multi-platform manifest is created

Pull and test the image:

```bash
docker pull ghcr.io/<owner>/melodee:X.Y.Z
```

### Step 7: Verify the About Page and Changelog

After deploying:

1. Navigate to the **About** page in the Melodee UI and confirm the displayed version matches the new `X.Y.Z`.
2. Navigate to the **Changelog** page on the docs site (`/changelog/`) and confirm the new version entry is visible.

## Version Display Locations

| Location | Source | Format |
|----------|--------|--------|
| About page | `IAppVersionProvider.GetSemVerForDisplay()` | `2.0.0` (prefix only) |
| Admin Dashboard → Server Stats | `Assembly.GetName().Version` | `2.0.0.0` |
| Admin Doctor → Server Info | `Assembly.GetName().Version` | `2.0.0.0` |
| Docker image tags | GitHub release tag | `2.0.0`, `2.0`, `2`, `latest` |
| Assembly metadata | `InformationalVersion` | `2.0.0+build20260501165851` |

## API Versioning (Separate from App Version)

Melodee uses `Asp.Versioning.Mvc` for REST API versioning, which is **independent** of the application SemVer.

- Current API version: `v1`
- Defined in `Program.cs` via `AddApiVersioning()`
- Consumers specify version via URL segment (`/api/v1/...`) or `X-Api-Version` header
- API version bumps are independent of application version bumps

## Automated Version Bumping (Future)

Consider adopting one of these tools for automated version management:

| Tool | Approach | Best For |
|------|----------|----------|
| **GitVersion** | Derives version from Git history/branches | Teams using GitFlow |
| **MinVer** | Uses Git tags as version source | Simple tag-based releases |
| **Nerdbank.GitVersioning** | `version.json` file + Git commits | Precise, deterministic builds |
| **Conventional Commits + release-please** | Auto-generates changelog + releases from commit messages | Automated CI/CD pipelines |

Using **MinVer** as an example, you could replace all manual .csproj version properties with:

```xml
<PackageReference Include="MinVer" Version="6.0.0" PrivateAssets="all" />
```

Then the version is derived entirely from Git tags (`v2.0.0` → assembly version `2.0.0`), eliminating the need to edit .csproj files during releases.
