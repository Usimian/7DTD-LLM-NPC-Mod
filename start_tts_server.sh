#!/bin/bash
# Start Piper TTS Server (foreground mode)
# Usage: ./start_tts_server.sh

echo "Starting Piper TTS Server..."
echo "=============================="

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Start Piper TTS Server
cd "$SCRIPT_DIR/piper-server"
python3 piper_server.py --port 5050
