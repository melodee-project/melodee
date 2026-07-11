#!/usr/bin/env bash
#
# bump_version.sh — propagate a new version across all Melodee projects.
#
# Usage:
#   ./scripts/bump_version.sh          # interactive prompt
#   ./scripts/bump_version.sh 2.3.0    # bump to 2.3.0
#   ./scripts/bump_version.sh --dry-run 2.3.0  # preview changes only
#
# This script updates:
#   - All 4 .csproj VersionPrefix values
#   - docs/pages/changelog.md (promotes [Unreleased] to the new version)
#   - docs/VERSION (if it exists)
#   - docs/_config.yml (sets the default documentation release)
#
# After merging the PR, create the git tag and GitHub Release from the tag.
#
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

DRY_RUN=false
VERSION=""

for arg in "$@"; do
  case "$arg" in
    --dry-run)  DRY_RUN=true ;;
    -h|--help)
      echo "Usage: $0 [--dry-run] [VERSION]"
      echo ""
      echo "Options:"
      echo "  --dry-run   Show what would change without modifying files"
      echo "  VERSION     Semantic version (e.g., 2.3.0). Prompts if omitted."
      exit 0
      ;;
    *)
      if [[ -z "$VERSION" ]]; then
        VERSION="$arg"
      else
        echo "Error: unexpected argument '$arg'" >&2
        exit 1
      fi
      ;;
  esac
done

# Prompt for version if not provided
if [[ -z "$VERSION" ]]; then
  # Get current version from first .csproj
  CURRENT_VERSION="$(grep -oP '(?<=<VersionPrefix>)[^<]+' src/Melodee.Blazor/Melodee.Blazor.csproj | head -1)"
  echo "Current version: $CURRENT_VERSION"
  read -rp "Enter new version (SemVer): " VERSION
fi

# Trim whitespace
VERSION="$(echo "$VERSION" | tr -d '[:space:]')"

# Validate semver
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-zA-Z0-9.]+)?$ ]]; then
  echo "Error: '$VERSION' is not a valid semver string" >&2
  exit 1
fi

# Determine current version
CURRENT_VERSION="$(grep -oP '(?<=<VersionPrefix>)[^<]+' src/Melodee.Blazor/Melodee.Blazor.csproj | head -1)"

echo ""
if [[ "$VERSION" == "$CURRENT_VERSION" ]]; then
  echo "Synchronizing version metadata at $VERSION"
else
  echo "Bumping version: $CURRENT_VERSION → $VERSION"
fi
echo ""

# Track updated files
updated=0
skipped=0

log_update() {
  local file="$1"
  local desc="$2"
  if [[ "$DRY_RUN" == true ]]; then
    echo "  [DRY] $file — $desc"
  else
    echo "  OK    $file — $desc"
  fi
  updated=$((updated + 1))
}

log_skip() {
  local file="$1"
  local reason="$2"
  echo "  SKIP  $file — $reason"
  skipped=$((skipped + 1))
}

version_list_contains() {
  local file="$1"
  local key="$2"
  local version="$3"

  awk -v key="$key" -v version="$version" '
    $0 == "  " key ":" { in_list = 1; next }
    in_list && $0 == "    - " version { found = 1; exit }
    in_list && $0 !~ /^    - / { exit }
    END { exit(found ? 0 : 1) }
  ' "$file"
}

# --- Update .csproj files ---
CSPROJ_FILES=(
  "src/Melodee.Blazor/Melodee.Blazor.csproj"
  "src/Melodee.Common/Melodee.Common.csproj"
  "src/Melodee.Cli/Melodee.Cli.csproj"
  "src/Melodee.Mql/Melodee.Mql.csproj"
)

for csproj in "${CSPROJ_FILES[@]}"; do
  if [[ ! -f "$csproj" ]]; then
    log_skip "$csproj" "file not found"
    continue
  fi

  old_ver="$(grep -oP '(?<=<VersionPrefix>)[^<]+' "$csproj" | head -1)"
  if [[ "$old_ver" == "$VERSION" ]]; then
    log_skip "$csproj" "already at $VERSION"
    continue
  fi

  if [[ "$DRY_RUN" == true ]]; then
    log_update "$csproj" "VersionPrefix: $old_ver → $VERSION"
  else
    sed -i "s|<VersionPrefix>${old_ver}</VersionPrefix>|<VersionPrefix>${VERSION}</VersionPrefix>|" "$csproj"
    log_update "$csproj" "VersionPrefix: $old_ver → $VERSION"
  fi
done

# --- Update docs/VERSION (if exists) ---
VERSION_FILE="docs/VERSION"
if [[ -f "$VERSION_FILE" ]]; then
  old_ver="$(cat "$VERSION_FILE" | tr -d '[:space:]')"
  if [[ "$old_ver" == "$VERSION" ]]; then
    log_skip "$VERSION_FILE" "already at $VERSION"
  elif [[ "$DRY_RUN" == true ]]; then
    log_update "$VERSION_FILE" "$old_ver → $VERSION"
  else
    echo "$VERSION" > "$VERSION_FILE"
    log_update "$VERSION_FILE" "$old_ver → $VERSION"
  fi
else
  log_skip "$VERSION_FILE" "file not found"
fi

# --- Update documentation release selector ---
DOCS_CONFIG="docs/_config.yml"
if [[ -f "$DOCS_CONFIG" ]]; then
  if ! grep -q '^  search_versions:$' "$DOCS_CONFIG" ||
     ! grep -q '^  latest:' "$DOCS_CONFIG" ||
     ! grep -q '^  versions:$' "$DOCS_CONFIG"; then
    echo "Error: $DOCS_CONFIG is missing required version_params keys" >&2
    exit 1
  fi

  old_latest="$(sed -n 's/^  latest:[[:space:]]*//p' "$DOCS_CONFIG" | head -1)"
  docs_config_synced=true
  if [[ "$old_latest" != "$VERSION" ]]; then
    docs_config_synced=false
  fi
  if ! version_list_contains "$DOCS_CONFIG" "search_versions" "$VERSION"; then
    docs_config_synced=false
  fi
  if ! version_list_contains "$DOCS_CONFIG" "versions" "$VERSION"; then
    docs_config_synced=false
  fi

  if [[ "$docs_config_synced" == true ]]; then
    log_skip "$DOCS_CONFIG" "default Release already set to $VERSION"
  elif [[ "$DRY_RUN" == true ]]; then
    log_update "$DOCS_CONFIG" "default Release: $old_latest → $VERSION"
  else
    sed -i "s|^  latest:.*$|  latest: ${VERSION}|" "$DOCS_CONFIG"
    if ! version_list_contains "$DOCS_CONFIG" "search_versions" "$VERSION"; then
      sed -i "/^  search_versions:$/a\\    - ${VERSION}" "$DOCS_CONFIG"
    fi
    if ! version_list_contains "$DOCS_CONFIG" "versions" "$VERSION"; then
      sed -i "/^  versions:$/a\\    - ${VERSION}" "$DOCS_CONFIG"
    fi
    log_update "$DOCS_CONFIG" "default Release: $old_latest → $VERSION"
  fi
else
  log_skip "$DOCS_CONFIG" "file not found"
fi

# --- Update changelog ---
CHANGELOG="docs/pages/changelog.md"
if [[ -f "$CHANGELOG" ]]; then
  if grep -Fq "## [${VERSION}] - " "$CHANGELOG"; then
    log_skip "$CHANGELOG" "already contains release $VERSION"
  elif [[ "$DRY_RUN" == true ]]; then
    log_update "$CHANGELOG" "promote [Unreleased] → [$VERSION]"
  else
    today="$(date +%Y-%m-%d)"

    # Check if [Unreleased] section exists
    if grep -q "^## \[Unreleased\]" "$CHANGELOG"; then
      # Keep the fresh Unreleased section at the existing content location so
      # Jekyll front matter remains the first block in the document.
      sed -i "s|^## \[Unreleased\]|## [Unreleased]\\n\\n## [${VERSION}] - ${today}|" "$CHANGELOG"

      log_update "$CHANGELOG" "promoted [Unreleased] → [$VERSION] - $today"
    else
      log_skip "$CHANGELOG" "no [Unreleased] section found"
    fi
  fi
else
  log_skip "$CHANGELOG" "file not found"
fi

echo ""

if [[ "$DRY_RUN" == true ]]; then
  echo "Dry run complete. $updated files would be updated."
  echo ""
  echo "Run without --dry-run to apply changes."
  exit 0
fi

echo "Updated $updated file(s) to version $VERSION"
echo ""

# --- Git status ---
echo "Changed files:"
git diff --name-only
echo ""

echo "Next steps:"
echo "  1. Commit and push: git commit -am \"chore: bump version to v${VERSION}\" && git push"
echo "  2. Open a PR and get it approved/merged"
echo "  3. After merge, create the tag: git tag -a v${VERSION} -m \"Release v${VERSION}\" && git push origin v${VERSION}"
echo "  4. Create GitHub Release from tag v${VERSION} (triggers Docker publish)"
echo "  5. Verify About page shows v${VERSION} in the UI"
echo "  6. Verify /changelog/ shows the new entry and the docs Release menu defaults to ${VERSION}"
