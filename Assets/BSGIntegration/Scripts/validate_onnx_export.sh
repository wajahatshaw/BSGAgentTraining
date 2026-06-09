#!/usr/bin/env bash
# Validate per-zone physical + cognitive ONNX brains exist after mlagents-learn.
# Usage: ./scripts/validate_onnx_export.sh <run-id>
set -euo pipefail
RUN_ID="${1:-persona_training}"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# Project root (Scripts -> BSGIntegration -> Assets -> repo root)
ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd)"
SRC="$ROOT/results/$RUN_ID"
MISSING=0

if [[ ! -d "$SRC" ]]; then
  echo "ERROR: Missing results folder: $SRC"
  exit 1
fi

echo "Checking ONNX exports under $SRC"
echo ""
echo "Physical brains:"
for z in 0 1 2 3; do
  name="PhysicalAgentZone${z}.onnx"
  found="$(find "$SRC" -maxdepth 3 -name "$name" -type f 2>/dev/null | head -1)"
  if [[ -n "${found}" ]]; then
    echo "  OK  $name -> $found"
  else
    echo "  MISSING  $name"
    MISSING=$((MISSING + 1))
  fi
done

echo ""
echo "Cognitive brains:"
for z in 0 1 2 3; do
  name="CognitiveAgentZone${z}.onnx"
  found="$(find "$SRC" -maxdepth 3 -name "$name" -type f 2>/dev/null | head -1)"
  if [[ -n "${found}" ]]; then
    echo "  OK  $name -> $found"
  else
    echo "  MISSING  $name"
    MISSING=$((MISSING + 1))
  fi
done

if [[ "$MISSING" -gt 0 ]]; then
  echo ""
  echo "FAIL: $MISSING brain(s) missing. Train with:"
  echo "  mlagents-learn config/worker_training_config_fixed.yaml --run-id=$RUN_ID"
  echo "Unity: enableMlTrainingInRagMode + enableMlTrainingForCognitiveAgents, Play until M_A spawns."
  exit 1
fi

echo ""
echo "PASS: all eight zone brains present (4 physical + 4 cognitive)."
echo "Copy into Unity:"
echo "  ./scripts/copy_onnx_to_unity.sh $RUN_ID"
echo "Assign Sentis ModelAssets on RagMlBrainDeployer (see Assets/ML-Models/RAG/brain_assignment_manifest.json)."
