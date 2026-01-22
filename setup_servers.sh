#!/bin/bash
# Setup script for NPC LLM Chat mod
# Installs Python dependencies for TTS and STT servers

echo "========================================"
echo "NPC LLM Chat - Server Setup"
echo "========================================"
echo ""

# Check if Python is installed
if ! command -v python3 &> /dev/null; then
    echo "ERROR: Python 3 is not installed"
    echo "Please install Python 3.9+ using your package manager"
    echo "Example: sudo apt-get install python3 python3-pip python3-venv"
    exit 1
fi

echo "Found Python:"
python3 --version
echo ""

# Setup Piper TTS server
echo "========================================"
echo "Setting up Piper TTS server..."
echo "========================================"
if [ -d "piper-server" ]; then
    cd piper-server
    
    # Create virtual environment if it doesn't exist
    if [ ! -d "venv" ]; then
        echo "Creating virtual environment..."
        python3 -m venv venv
    fi
    
    # Install dependencies
    echo "Installing Piper TTS dependencies..."
    source venv/bin/activate
    pip install --upgrade pip
    pip install -r requirements.txt
    pip install piper-tts
    deactivate
    
    echo "Piper TTS setup complete!"
    cd ..
else
    echo "WARNING: piper-server directory not found, skipping..."
fi
echo ""

# Setup Whisper STT server
echo "========================================"
echo "Setting up Whisper STT server..."
echo "========================================"
if [ -d "whisper-server" ]; then
    cd whisper-server
    
    # Create virtual environment if it doesn't exist
    if [ ! -d "venv" ]; then
        echo "Creating virtual environment..."
        python3 -m venv venv
    fi
    
    # Install dependencies
    echo "Installing Whisper STT dependencies..."
    source venv/bin/activate
    pip install --upgrade pip
    pip install -r requirements.txt
    deactivate
    
    echo "Whisper STT setup complete!"
    cd ..
else
    echo "WARNING: whisper-server directory not found, skipping..."
fi
echo ""

echo "========================================"
echo "Setup Complete!"
echo "========================================"
echo ""
echo "The mod is now ready to use."
echo ""
echo "Next steps:"
echo "1. Make sure Ollama is installed: https://ollama.com/download"
echo "2. Download an AI model: ollama pull gemma3:4b"
echo "3. Launch 7 Days to Die"
echo ""
echo "The mod will automatically start all servers when the game loads."
echo ""
