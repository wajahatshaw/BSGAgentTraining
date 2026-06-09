#!/bin/bash
# Generate ML Model Results After Training
# This script runs after ML-Agents training completes (similar to ONNX generation)
# Usage: ./generate_ml_results_after_training.sh [run-id]

set -e

# Get run ID from argument or use default
RUN_ID=${1:-bsg_training_new}

echo "📊 Generating ML model analysis results for run: $RUN_ID"

# Get the directory where this script is located
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
PROJECT_ROOT="$( cd "$SCRIPT_DIR/../../.." && pwd )"

# Path to Python script
PYTHON_SCRIPT="$PROJECT_ROOT/BSGServer/generate_ml_model_results.py"

# Activate ML-Agents environment (if exists)
if [ -d "$PROJECT_ROOT/ml-agents-env" ]; then
    source "$PROJECT_ROOT/ml-agents-env/bin/activate"
elif [ -d "$PROJECT_ROOT/venv" ]; then
    source "$PROJECT_ROOT/venv/bin/activate"
fi

# Check if Python script exists
if [ ! -f "$PYTHON_SCRIPT" ]; then
    echo "❌ Error: Python script not found at $PYTHON_SCRIPT"
    exit 1
fi

# Run the Python script
echo "🚀 Running ML model results generator..."
python3 "$PYTHON_SCRIPT" --run-id="$RUN_ID"

# Check exit status
if [ $? -eq 0 ]; then
    echo "✅ ML model results generation completed successfully!"
    exit 0
else
    echo "❌ ML model results generation failed!"
    exit 1
fi

