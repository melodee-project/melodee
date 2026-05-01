#!/bin/bash

# Melodee CLI wrapper script
# Sets the environment and runs the CLI command

# Set default environment if not already set
if [ -z "$MELODEE_ENVIRONMENT" ] && [ -z "$ASPNETCORE_ENVIRONMENT" ]; then
    export MELODEE_ENVIRONMENT=${MELODEE_CLI_DEFAULT_ENV:-Development}
fi

# Determine the script directory to build relative paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Path to the CLI executable (relative to the script location)
CLI_PATH="${MELODEE_CLI_PATH:-$SCRIPT_DIR/../src/Melodee.Cli/bin/Debug/net10.0/mcli}"

# Check if the CLI exists at the computed path
if [ ! -f "$CLI_PATH" ]; then
    echo "Error: CLI executable not found at $CLI_PATH"
    echo "Please build the project first with: dotnet build src/Melodee.Cli"
    exit 1
fi

# Run the CLI from its own directory to ensure it can find its configuration files
cd "$(dirname "$CLI_PATH")" && exec "./$(basename "$CLI_PATH")" "$@"
