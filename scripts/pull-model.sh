#!/bin/sh
# Downloads the receipt-reading model into Ollama, unless it's already there.
#   Docker: run by the ollama-pull service in docker-compose.yml
#   Linux/macOS: scripts/pull-model.sh [model]      Windows: scripts\pull-model.ps1
# Model: the first argument, else $MODEL, else the default in appsettings.json.
set -eu

MODEL="${1:-${MODEL:-qwen3-vl:8b-instruct}}"

if ! ollama list >/dev/null 2>&1; then
    echo "Can't reach Ollama${OLLAMA_HOST:+ at $OLLAMA_HOST}. Start it, then run this again." >&2
    exit 1
fi

if ollama list | awk 'NR > 1 { print $1 }' | grep -qxF "$MODEL"; then
    echo "$MODEL is already downloaded."
    exit 0
fi

echo "Downloading $MODEL (qwen3-vl:8b-instruct is about 6 GB)..."
ollama pull "$MODEL"
echo "Done: $MODEL is ready. Receipts can be read now."
