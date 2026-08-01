#!/bin/bash
# Setup script for NPC LLM Chat mod
# Builds the Python environments the TTS and STT servers need.
# Run it from the folder that holds piper-server and whisper-server.

echo "========================================"
echo "NPC LLM Chat - Server Setup"
echo "========================================"
echo ""

if [ ! -d "piper-server" ] && [ ! -d "whisper-server" ]; then
    echo "ERROR: piper-server and whisper-server are not in this folder."
    echo "Run this script from the folder that contains them."
    exit 1
fi

if ! command -v python3 &> /dev/null; then
    echo "ERROR: Python 3 is not installed"
    echo "Please install Python 3.9+ using your package manager"
    echo "Example: sudo apt-get install python3 python3-pip python3-venv"
    exit 1
fi

echo "Found Python:"
python3 --version
echo ""

# A venv is tied to the interpreter that built it: it records that absolute path
# and symlinks the binary. So it breaks when it is copied from another machine,
# and equally when the system python is upgraded out from under it - 3.12 to 3.13
# leaves a venv that looks present and cannot run. Test before trusting it, since
# installing into a dead venv just leaves it dead.
setup_server() {
    local dir="$1" name="$2" extra="$3"

    echo "========================================"
    echo "Setting up $name server..."
    echo "========================================"

    if [ ! -d "$dir" ]; then
        echo "WARNING: $dir directory not found, skipping..."
        echo ""
        return 0
    fi

    (
        cd "$dir" || exit 1

        if [ -d "venv" ] && ! venv/bin/python -c "import sys" &> /dev/null; then
            echo "Existing environment cannot run here - rebuilding it..."
            rm -rf venv
        fi

        if [ ! -d "venv" ]; then
            echo "Creating virtual environment..."
            if ! python3 -m venv venv; then
                echo "ERROR: could not create the virtual environment for $name."
                echo "On Debian/Ubuntu you may need: sudo apt-get install python3-venv"
                exit 1
            fi
        fi

        echo "Installing $name dependencies..."
        source venv/bin/activate
        pip install --upgrade pip
        pip install -r requirements.txt || exit 1
        [ -n "$extra" ] && { pip install "$extra" || exit 1; }
        deactivate

        echo "$name setup complete!"
    ) || { echo "$name setup FAILED."; echo ""; return 1; }

    echo ""
    return 0
}

failed=0
setup_server "piper-server"   "Piper TTS"   "piper-tts" || failed=1
setup_server "whisper-server" "Whisper STT" ""          || failed=1

echo "========================================"
if [ "$failed" -eq 0 ]; then
    echo "Setup Complete!"
else
    echo "Setup finished WITH ERRORS - see the messages above."
fi
echo "========================================"
echo ""
echo "Next steps:"
echo "1. Make sure Ollama is installed: https://ollama.com/download"
echo "2. Download an AI model: ollama pull gemma3:4b"
echo "3. Launch 7 Days to Die"
echo ""
echo "The mod will automatically start all servers when the game loads."
echo ""

exit "$failed"
