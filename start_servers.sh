#!/bin/bash
# Start both TTS (Piper) and STT (Whisper) servers for NPC LLM Chat
# Usage: ./start_servers.sh

echo "Starting NPC LLM Chat Audio Servers..."
echo "======================================="

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Start Piper TTS Server
echo ""
echo "Starting Piper TTS Server (port 5050)..."
cd "$SCRIPT_DIR/piper-server"
python3 piper_server.py --port 5050 &
TTS_PID=$!
echo "  PID: $TTS_PID"

# Start Whisper STT Server
echo ""
echo "Starting Whisper STT Server (port 5051)..."
cd "$SCRIPT_DIR/whisper-server"
source venv/bin/activate
python whisper_server.py --port 5051 --preload &
STT_PID=$!
echo "  PID: $STT_PID"

# Wait a moment for servers to start
sleep 2

# Check if servers are running
echo ""
echo "Checking server status..."
if ps -p $TTS_PID > /dev/null 2>&1; then
    echo "  ✓ TTS Server running (PID: $TTS_PID)"
else
    echo "  ✗ TTS Server failed to start"
fi

if ps -p $STT_PID > /dev/null 2>&1; then
    echo "  ✓ STT Server running (PID: $STT_PID)"
else
    echo "  ✗ STT Server failed to start"
fi

echo ""
echo "Servers started in background."
echo "To stop them later, run:"
echo "  kill $TTS_PID $STT_PID"
echo ""
echo "Or save PIDs to file:"
cat > "$SCRIPT_DIR/.server_pids" << EOF
TTS_PID=$TTS_PID
STT_PID=$STT_PID
EOF
echo "  Saved to .server_pids"
echo ""
echo "To stop servers later: ./stop_servers.sh"
