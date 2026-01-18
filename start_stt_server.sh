#!/bin/bash
# Start Whisper STT Server (foreground mode)
# Usage: ./start_stt_server.sh [--model MODEL]
# Example: ./start_stt_server.sh --model small.en

echo "Starting Whisper STT Server..."
echo "==============================="

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Start Whisper STT Server
cd "$SCRIPT_DIR/whisper-server"
source venv/bin/activate
python whisper_server.py --port 5051 --preload "$@"
