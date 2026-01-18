#!/bin/bash
# Start Whisper STT Server
# Usage: ./start_server.sh [--port PORT] [--model MODEL]

cd "$(dirname "$0")"

# Activate virtual environment
source venv/bin/activate

# Start server with default or provided arguments
python whisper_server.py --port 5051 --preload "$@"
