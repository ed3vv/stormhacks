#!/bin/zsh
set -e
cd "${0:A:h}"
python3 -m http.server 8000 --bind 127.0.0.1 &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT INT TERM
sleep 1
open "http://127.0.0.1:8000/"
wait "$server_pid"
