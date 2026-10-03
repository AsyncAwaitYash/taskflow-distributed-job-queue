#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet build TaskFlow.slnx
dotnet test TaskFlow.slnx --no-build
