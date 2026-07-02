#!/bin/bash
set -e

# Get absolute path to script directory
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
TRANSIENT_LIB_CSPROJ="$SCRIPT_DIR/../NATS.Client.CheckAbiTransientLib/NATS.Client.CheckAbiTransientLib.csproj"
CHECK_ABI_CSPROJ="$SCRIPT_DIR/NATS.Client.CheckAbi.csproj"
CHECK_ABI_OLD_CSPROJ="$SCRIPT_DIR/../NATS.Client.CheckAbiOld/NATS.Client.CheckAbiOld.csproj"

VERSION=$(cat "$SCRIPT_DIR/../../version.txt" | tr -d '[:space:]')
OLD_VERSION=$(grep 'NatsAbiCheckVersion' "$SCRIPT_DIR/../../Directory.Build.props" | sed 's/.*>\([^<]*\)<.*/\1/')

echo "=== NATS.Net ABI Compatibility Check (Transient Dependency Simulation) ==="
echo ""
echo "This test simulates:"
echo "  - An app using local NATS.Net $VERSION (with type forwarders)"
echo "  - A transient dependency (TransientLib) compiled against NATS.Net $OLD_VERSION"
echo "  - Type forwarding should allow the old library to work with new NATS.Net"
echo ""

# Step 1: Build CheckAbiTransientLib against NuGet package
echo "[1/4] Building NATS.Client.CheckAbiTransientLib against NATS.Net $OLD_VERSION..."
dotnet clean
dotnet build "$TRANSIENT_LIB_CSPROJ" -c Release

# Step 2: Build and run CheckAbiOld (control: everything is 2.6.0)
echo ""
echo "[2/4] Building and running CheckAbiOld (control, NATS.Net $OLD_VERSION NuGet)..."
dotnet build "$CHECK_ABI_OLD_CSPROJ" -c Release

OLD_OUTPUT=$(dotnet run --project "$CHECK_ABI_OLD_CSPROJ" -c Release --no-build)
echo "$OLD_OUTPUT"
echo ""

# Verify old project sees types in NATS.Client.Core with the old version
if ! echo "$OLD_OUTPUT" | grep -q "NATS.Client.Core v${OLD_VERSION}.0"; then
    echo "FAILED: CheckAbiOld should load NATS.Client.Core v${OLD_VERSION}.0"
    exit 1
fi
if echo "$OLD_OUTPUT" | grep -q "NATS.Client.Abstractions"; then
    echo "FAILED: CheckAbiOld should NOT reference NATS.Client.Abstractions"
    exit 1
fi
echo "OK: CheckAbiOld correctly uses NATS.Client.Core v${OLD_VERSION}.0"

# Step 3: Build AbiCheck (references local 2.7.0 source + TransientLib.dll)
echo ""
echo "[3/4] Building AbiCheck against local NATS.Net source + TransientLib..."
dotnet build "$CHECK_ABI_CSPROJ" -c Release

# Step 4: Run the test
echo ""
echo "[4/4] Running ABI compatibility check..."
echo ""

NEW_OUTPUT=$(dotnet run --project "$CHECK_ABI_CSPROJ" -c Release --no-build)
echo "$NEW_OUTPUT"
echo ""

# Verify new project sees types forwarded to NATS.Client.Abstractions
if ! echo "$NEW_OUTPUT" | grep -q "NATS.Client.Abstractions"; then
    echo "FAILED: CheckAbi should resolve types to NATS.Client.Abstractions"
    exit 1
fi
if echo "$NEW_OUTPUT" | grep -q "NATS.Client.Core v${OLD_VERSION}.0"; then
    echo "FAILED: CheckAbi should NOT load NATS.Client.Core v${OLD_VERSION}.0"
    exit 1
fi
if ! echo "$NEW_OUTPUT" | grep -q "SUCCESS"; then
    echo "FAILED: ABI compatibility check did not succeed"
    exit 1
fi

echo ""
echo "=== ABI Check Complete ==="
