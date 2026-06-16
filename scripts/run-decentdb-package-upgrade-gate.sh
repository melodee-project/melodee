#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'USAGE'
Usage:
  ./scripts/run-decentdb-package-upgrade-gate.sh [--options]

This helper runs Melodee's existing MusicBrainz import/query probes and evaluates
DDB-002/DDB-003 acceptance for an existing checkpointed database.

Required:
  --db PATH         Path to checkpointed MusicBrainz .ddb file.

Options:
  --storage PATH    Path to MusicBrainz staging dump for optional import rebuild.
  --label TEXT      Identifier for output subdirectory (default: candidate).
  --output-dir PATH Output root (default: .tmp/decentdb-upgrade-validation).
  --max-warm-name-ms N   Max warm Artist.NameNormalized query time in ms (default: 1000).
  --max-warm-mbid-ms N   Max warm Artist.MusicBrainzIdRaw query time in ms (default: 1000).
  --max-warm-alias-ms N  Max warm ArtistAlias.NameNormalized query time in ms (default: 2000).
  --name TEXT      Explicit normalized artist sample for the query probe.
  --alias TEXT     Explicit normalized alias sample for the query probe.
  --mbid TEXT      Explicit raw MusicBrainz ID sample for the query probe.
  --include-row-existence
                   Include the broad first-row existence probe for investigation.
  --skip-build      Skip restore/build step and run probes directly.
  --help            Show this help text.

Examples:
  ./scripts/run-decentdb-package-upgrade-gate.sh \
    --db /tmp/musicbrainz.ddb \
    --label 2.13.1 \
    --output-dir .tmp/decentdb-upgrade

  ./scripts/run-decentdb-package-upgrade-gate.sh \
    --db /tmp/musicbrainz.ddb \
    --storage /mnt/fileserver_db_storage/melodee/search-engine-storage/musicbrainz \
    --label 2.13.1-candidate \
    --output-dir .tmp/decentdb-upgrade
USAGE
}

if [[ $# -eq 0 ]]; then
    usage
    exit 1
fi

DB_PATH=""
STORAGE_PATH=""
LABEL="candidate"
OUTPUT_DIR=".tmp/decentdb-upgrade-validation"
MAX_NAME_MS=1000
MAX_MBID_MS=1000
MAX_ALIAS_MS=2000
SAMPLE_NAME=""
SAMPLE_ALIAS=""
SAMPLE_MBID=""
INCLUDE_ROW_EXISTENCE=false
SKIP_BUILD=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --db)
            DB_PATH="$2"
            shift 2
            ;;
        --storage)
            STORAGE_PATH="$2"
            shift 2
            ;;
        --label)
            LABEL="$2"
            shift 2
            ;;
        --output-dir)
            OUTPUT_DIR="$2"
            shift 2
            ;;
        --max-warm-name-ms)
            MAX_NAME_MS="$2"
            shift 2
            ;;
        --max-warm-mbid-ms)
            MAX_MBID_MS="$2"
            shift 2
            ;;
        --max-warm-alias-ms)
            MAX_ALIAS_MS="$2"
            shift 2
            ;;
        --name)
            SAMPLE_NAME="$2"
            shift 2
            ;;
        --alias)
            SAMPLE_ALIAS="$2"
            shift 2
            ;;
        --mbid)
            SAMPLE_MBID="$2"
            shift 2
            ;;
        --include-row-existence)
            INCLUDE_ROW_EXISTENCE=true
            shift
            ;;
        --skip-build)
            SKIP_BUILD=true
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1" >&2
            usage
            exit 1
            ;;
    esac
done

if [[ -z "$DB_PATH" ]]; then
    echo "Error: --db is required." >&2
    usage
    exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "Error: jq is required for acceptance checks." >&2
    exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Error: dotnet is required to run benchmark probes." >&2
    exit 1
fi

RUN_DIR="${OUTPUT_DIR%/}/${LABEL}"
IMPORT_REPORT="${RUN_DIR}/musicbrainz-import-probe.json"
CHECKPOINTED_REPORT="${RUN_DIR}/musicbrainz-query-probe-checkpointed.json"
mkdir -p "$RUN_DIR"

if [[ "$SKIP_BUILD" != true ]]; then
    echo "Restoring and building benchmark project..."
    dotnet restore Melodee.sln
    dotnet build Melodee.sln --no-restore
fi

if [[ -n "$STORAGE_PATH" ]]; then
    if [[ ! -d "$STORAGE_PATH" ]]; then
        echo "Error: --storage path does not exist: $STORAGE_PATH" >&2
        exit 1
    fi

    if [[ ! -d "$STORAGE_PATH/staging/mbdump" ]]; then
        echo "Error: expected MusicBrainz dump under $STORAGE_PATH/staging/mbdump." >&2
        exit 1
    fi

    echo "Running import probe for fresh real-file rebuild..."
    dotnet run -c Release --project benchmarks/Melodee.Benchmarks -- \
        musicbrainz-import-probe \
        --storage "$STORAGE_PATH" \
        --db "$DB_PATH" \
        --output "$IMPORT_REPORT" \
        --clean \
        --sample-interval-ms 10000

    echo "Import probe complete -> $IMPORT_REPORT"
fi

echo "Running checkpointed query probe..."
QUERY_PROBE_ARGS=(
    musicbrainz-query-probe
    --db "$DB_PATH"
    --output "$CHECKPOINTED_REPORT"
)

if [[ -n "$SAMPLE_NAME" ]]; then
    QUERY_PROBE_ARGS+=(--name "$SAMPLE_NAME")
fi

if [[ -n "$SAMPLE_ALIAS" ]]; then
    QUERY_PROBE_ARGS+=(--alias "$SAMPLE_ALIAS")
fi

if [[ -n "$SAMPLE_MBID" ]]; then
    QUERY_PROBE_ARGS+=(--mbid "$SAMPLE_MBID")
fi

if [[ "$INCLUDE_ROW_EXISTENCE" == true ]]; then
    QUERY_PROBE_ARGS+=(--include-row-existence)
fi

dotnet run -c Release --project benchmarks/Melodee.Benchmarks -- \
    "${QUERY_PROBE_ARGS[@]}"

if [[ ! -f "$CHECKPOINTED_REPORT" ]]; then
    echo "Error: query probe report not created: $CHECKPOINTED_REPORT" >&2
    exit 1
fi

check_query_ms() {
    local query_name="$1"
    local max_ms="$2"
    local report_path="$3"

    local warm_ms
    warm_ms=$(jq -r --arg name "$query_name" --arg pass "warm" \
        '.Measurements[] | select(.Name == $name and .Pass == $pass) | .ElapsedMilliseconds' "$report_path" | head -n 1)

    if [[ -z "$warm_ms" || "$warm_ms" == "null" ]]; then
        echo "$query_name: missing warm timing"
        return 1
    fi

    local plan
    plan=$(jq -r --arg name "$query_name" --arg pass "warm" \
        '.Measurements[] | select(.Name == $name and .Pass == $pass) | .PlanDiagnostics.Lines | join(" ")' "$report_path")

    if ! echo "$plan" | grep -q "IndexSeek"; then
        echo "$query_name: warm $warm_ms ms -> FAIL (plan missing IndexSeek)"
        return 1
    fi

    if awk -v ms="$warm_ms" -v limit="$max_ms" 'BEGIN { exit !(ms <= limit) }'; then
        echo "$query_name: warm $warm_ms ms (PASS <= ${max_ms}ms, plan captured)"
        return 0
    fi

    echo "$query_name: warm $warm_ms ms (FAIL > ${max_ms}ms)"
    return 1
}

FAILURES=0

if ! check_query_ms "exact-normalized-name" "$MAX_NAME_MS" "$CHECKPOINTED_REPORT"; then
    FAILURES=1
fi

if ! check_query_ms "exact-musicbrainz-id-raw" "$MAX_MBID_MS" "$CHECKPOINTED_REPORT"; then
    FAILURES=1
fi

if ! check_query_ms "exact-normalized-alias" "$MAX_ALIAS_MS" "$CHECKPOINTED_REPORT"; then
    echo "Alias shape check failed but does not block DDB-002/DDB-003." >&2
fi

echo "Query probe report: $CHECKPOINTED_REPORT"

if [[ "$FAILURES" -ne 0 ]]; then
    echo "DDB-002 / DDB-003 gate failed. See report output for details." >&2
    exit 1
fi

echo "DDB-002 / DDB-003 gate passed."
