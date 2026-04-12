#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

cd "$REPO_ROOT"

contract_root="src/Aion.Domain/"
version_file="docs/ARCHITECTURE_FREEZE_V1.md"
base_branch="${GITHUB_BASE_REF:-main}"
base_ref="origin/${base_branch}"

if ! git fetch origin "$base_branch" --depth=1; then
  echo "Warning: Unable to fetch ${base_ref} for contract validation; falling back to HEAD~1." >&2
fi

merge_base="$(git merge-base HEAD "$base_ref" 2>/dev/null || true)"
if [[ -z "${merge_base}" ]]; then
  merge_base="HEAD~1"
fi

changed_files="$(git diff --name-only "${merge_base}" HEAD || true)"
if [[ -n "${changed_files}" ]] && echo "${changed_files}" | grep -q "^${contract_root}"; then
  if ! echo "${changed_files}" | grep -q "^${version_file}$"; then
    echo "Contract changes detected in '${contract_root}' without version bump in '${version_file}'." >&2
    echo "${changed_files}" | grep "^${contract_root}" >&2
    exit 1
  fi
fi

dotnet restore AionMemory.slnx
dotnet build AionMemory.slnx --configuration Release --no-restore /p:ContinuousIntegrationBuild=true
dotnet test AionMemory.slnx --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"
