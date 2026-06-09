#!/usr/bin/env bash
# Train a single RAG zone (0–3) with mlagents-learn.
# Unity: set ReplicaSceneSetup.trainZonesMask = 2^zone (e.g. zone 0 → 1, zone 2 → 4).
# Usage: ./scripts/train_zone.sh <zoneIndex 0-3> [run-id]
set -euo pipefail
ZONE="${1:-}"
RUN_ID="${2:-persona_zone${ZONE}}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
MASK=$((1 << ZONE))

if [[ -z "$ZONE" ]] || [[ "$ZONE" -lt 0 ]] || [[ "$ZONE" -gt 3 ]]; then
  echo "Usage: $0 <zoneIndex 0-3> [run-id]"
  exit 1
fi

echo "Zone $ZONE training"
echo "  Unity trainZonesMask bit value: $MASK (set on ReplicaSceneSetup before Play)"
echo "  YAML behaviors: PhysicalAgentZone${ZONE}, CognitiveAgentZone${ZONE}"
echo "  Expected ONNX: PhysicalAgentZone${ZONE}.onnx + CognitiveAgentZone${ZONE}.onnx"
echo ""
echo "Start Unity Play with enableMlTrainingInRagMode, enableMlTrainingForCognitiveAgents, trainZonesMask=$MASK, then run:"
echo "  cd \"$ROOT\" && mlagents-learn config/worker_training_config_fixed.yaml --run-id=${RUN_ID}"
echo ""
echo "After training:"
echo "  ./scripts/validate_onnx_export.sh ${RUN_ID}"
echo "  ./scripts/copy_onnx_to_unity.sh ${RUN_ID}"
