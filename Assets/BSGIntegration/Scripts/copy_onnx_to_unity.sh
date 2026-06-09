#!/usr/bin/env bash
# Copy exported ONNX brains (physical + cognitive) from mlagents-learn results into Unity.
# Usage: ./scripts/copy_onnx_to_unity.sh persona_training
set -euo pipefail
RUN_ID="${1:-persona_training}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
SRC="$ROOT/results/$RUN_ID"
DST_INFERENCE="$ROOT/Assets/ML-Agents/Models/Inference"
DST_RAG="$ROOT/Assets/ML-Models/RAG"

if [[ ! -d "$SRC" ]]; then
  echo "Missing results folder: $SRC"
  exit 1
fi

mkdir -p "$DST_INFERENCE" "$DST_RAG"
for z in 0 1 2 3; do
  for prefix in PhysicalAgentZone CognitiveAgentZone; do
    name="${prefix}${z}.onnx"
    behavior="${prefix}${z}"
    found=""
    if [[ -f "$SRC/$name" ]]; then
      found="$SRC/$name"
    elif [[ -d "$SRC/$behavior" ]]; then
      found="$(find "$SRC/$behavior" -name "${behavior}-*.onnx" -type f 2>/dev/null | sort -t- -k2 -n | tail -1)"
      [[ -z "$found" ]] && found="$(find "$SRC/$behavior" -name "*.onnx" -type f 2>/dev/null | head -1)"
    fi
    if [[ -n "${found}" ]]; then
      for DST in "$DST_INFERENCE" "$DST_RAG"; do
        cp -f "$found" "$DST/$name"
        rm -f "$DST/$name.meta"
        echo "Copied $found -> $DST/$name"
      done
    else
      echo "WARN: $name not found under $SRC"
    fi
  done
done
echo "In Unity: BSG → Inference → Fix ONNX Import (reimport + relink deployer)."
