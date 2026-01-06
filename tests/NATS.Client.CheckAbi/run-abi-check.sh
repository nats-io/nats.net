#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

echo "=== NATS.Net ABI Compatibility Check (Transient Dependency Simulation) ==="
echo ""
echo "This test simulates:"
echo "  - An app using NATS.Net 2.7.0 (with type forwarders)"
echo "  - A transient dependency (TransientLib) compiled against NATS.Net 2.6.0"
echo "  - Type forwarding should allow the old library to work with new NATS.Net"
echo ""

# Step 1: Build CheckAbiTransientLib against 2.6.0 NuGet package
echo "[1/3] Building NATS.Client.CheckAbiTransientLib against NATS.Net 2.6.0..."
dotnet build "$SCRIPT_DIR/../NATS.Client.CheckAbiTransientLib/NATS.Client.CheckAbiTransientLib.csproj" -c Release

# Step 2: Build AbiCheck (references local 2.7.0 source + TransientLib.dll)
echo ""
echo "[2/3] Building AbiCheck against local NATS.Net 2.7.0 source + TransientLib..."
dotnet build "$SCRIPT_DIR/NATS.Client.CheckAbi.csproj" -c Release

# Step 3: Run the test
echo ""
echo "[3/3] Running ABI compatibility check..."
echo ""
dotnet run --project "$SCRIPT_DIR/NATS.Client.CheckAbi.csproj" -c Release --no-build

echo ""
echo "=== ABI Check Complete ==="
