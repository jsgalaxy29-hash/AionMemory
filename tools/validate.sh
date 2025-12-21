#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "$REPO_ROOT"

dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true
dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
