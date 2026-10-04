#!/usr/bin/env bash
set -euo pipefail

if [[ "${1:-}" == "research-client" ]]; then
    shift
    exec mono /app/ResearchClient/ResearchClient.exe "$@"
fi

pids=()

start_server() {
    local directory="$1"
    local executable="$2"
    (
        cd "$directory"
        exec mono "$executable"
    ) &
    pids+=("$!")
}

stop_servers() {
    trap - INT TERM
    for pid in "${pids[@]}"; do
        kill -INT "$pid" 2>/dev/null || true
    done
    wait "${pids[@]}" 2>/dev/null || true
}

trap stop_servers INT TERM EXIT

start_server /app/WorldServer WorldServer.exe
start_server /app/LoginServer LoginServer.exe
start_server /app/MapServer MapServer.exe

echo "ECO servers started: World=17831 Login=17832 Map=17833"

wait -n "${pids[@]}"
status=$?
echo "An ECO server stopped with status ${status}; stopping the remaining servers." >&2
exit "$status"
