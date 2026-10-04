#!/usr/bin/env bash
set -euo pipefail

exec mono /app/ResearchClient/ResearchClient.exe "$@"
