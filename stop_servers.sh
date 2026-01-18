#!/bin/bash
# Stop TTS and STT servers for NPC LLM Chat
# Usage: ./stop_servers.sh

echo "Stopping NPC LLM Chat Audio Servers..."
echo "======================================="

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Check if PID file exists
if [ -f "$SCRIPT_DIR/.server_pids" ]; then
    source "$SCRIPT_DIR/.server_pids"

    if [ -n "$TTS_PID" ]; then
        if ps -p $TTS_PID > /dev/null 2>&1; then
            echo "Stopping TTS Server (PID: $TTS_PID)..."
            kill $TTS_PID 2>/dev/null
            echo "  ✓ TTS Server stopped"
        else
            echo "  TTS Server (PID: $TTS_PID) not running"
        fi
    fi

    if [ -n "$STT_PID" ]; then
        if ps -p $STT_PID > /dev/null 2>&1; then
            echo "Stopping STT Server (PID: $STT_PID)..."
            kill $STT_PID 2>/dev/null
            echo "  ✓ STT Server stopped"
        else
            echo "  STT Server (PID: $STT_PID) not running"
        fi
    fi

    rm "$SCRIPT_DIR/.server_pids"
    echo ""
    echo "Servers stopped."
else
    echo "No .server_pids file found."
    echo "Searching for server processes manually..."

    # Try to find and kill piper_server.py
    TTS_PIDS=$(pgrep -f "piper_server.py")
    if [ -n "$TTS_PIDS" ]; then
        echo "Found TTS Server processes: $TTS_PIDS"
        kill $TTS_PIDS 2>/dev/null
        echo "  ✓ Stopped TTS Server"
    else
        echo "  No TTS Server found"
    fi

    # Try to find and kill whisper_server.py
    STT_PIDS=$(pgrep -f "whisper_server.py")
    if [ -n "$STT_PIDS" ]; then
        echo "Found STT Server processes: $STT_PIDS"
        kill $STT_PIDS 2>/dev/null
        echo "  ✓ Stopped STT Server"
    else
        echo "  No STT Server found"
    fi
fi
