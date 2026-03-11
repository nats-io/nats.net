#!/bin/bash
# API Compatibility Check Script
# Verifies that APIs are compatible across different TFMs
# Usage: ./scripts/apicompat.sh [--build]

set -e

# Get script directory (works on both Windows and Linux)
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"

PROJECTS="NATS.Client.Abstractions NATS.Client.Core NATS.Client.Hosting NATS.Client.JetStream NATS.Client.KeyValueStore NATS.Client.ObjectStore NATS.Client.Services NATS.Client.Serializers.Json NATS.Client.Simplified NATS.Net NATS.Extensions.Microsoft.DependencyInjection"

SUPPRESSION_FILE="$ROOT_DIR/apicompat.suppression.xml"

# Check if apicompat is installed
if ! command -v apicompat &> /dev/null; then
    echo "Installing Microsoft.DotNet.ApiCompat.Tool..."
    dotnet tool install --global Microsoft.DotNet.ApiCompat.Tool
fi

# Build if requested
if [ "$1" = "--build" ]; then
    echo "Building solution..."
    dotnet build -c Release "$ROOT_DIR/NATS.Net.sln"
fi

check_compat() {
    local baseline_tfm=$1
    local target_tfm=$2
    local failed=0

    echo ""
    echo "=============================================="
    echo "Checking: $baseline_tfm (baseline) vs $target_tfm"
    echo "=============================================="

    for project in $PROJECTS; do
        left="$ROOT_DIR/src/$project/bin/Release/$baseline_tfm/$project.dll"
        right="$ROOT_DIR/src/$project/bin/Release/$target_tfm/$project.dll"

        if [ -f "$left" ] && [ -f "$right" ]; then
            echo -n "  $project: "
            if apicompat --left "$left" --right "$right" --suppression-file "$SUPPRESSION_FILE" --permit-unnecessary-suppressions > /dev/null 2>&1; then
                echo "OK"
            else
                echo "FAILED"
                apicompat --left "$left" --right "$right" --suppression-file "$SUPPRESSION_FILE" --permit-unnecessary-suppressions 2>&1 | head -20
                failed=1
            fi
        else
            echo "  $project: SKIPPED (assemblies not found)"
        fi
    done

    return $failed
}

echo "API Compatibility Check"
echo "======================="

exit_code=0

# Only check against net8.0 as the target TFM
# net6.0 has known differences with init accessors
check_compat "netstandard2.0" "net8.0" || exit_code=1
check_compat "netstandard2.1" "net8.0" || exit_code=1

echo ""
if [ $exit_code -eq 0 ]; then
    echo "All API compatibility checks passed!"
else
    echo "Some API compatibility checks failed!"
fi

exit $exit_code
