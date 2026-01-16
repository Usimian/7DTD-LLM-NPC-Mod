#!/bin/bash
# Quick setup script for Ollama with 7DTD NPC LLM Chat

echo "=== NPC LLM Chat - Ollama Setup ==="
echo ""

# Check if Ollama is installed
if command -v ollama &> /dev/null; then
    echo "[✓] Ollama is installed"
    ollama --version
else
    echo "[!] Ollama not found. Installing..."
    curl -fsSL https://ollama.com/install.sh | sh
fi

echo ""
echo "=== Pulling Recommended Models ==="
echo ""

# Ask user which model to use
echo "Which model would you like to use?"
echo "  1) llama3 (8B) - Best quality, needs 8GB+ RAM"
echo "  2) gemma2:2b   - Fastest, works on most systems"
echo "  3) mistral     - Good balance of speed/quality"
echo "  4) All of the above"
echo ""
read -p "Enter choice [1-4]: " choice

case $choice in
    1)
        ollama pull llama3
        MODEL="llama3"
        ;;
    2)
        ollama pull gemma2:2b
        MODEL="gemma2:2b"
        ;;
    3)
        ollama pull mistral
        MODEL="mistral"
        ;;
    4)
        ollama pull llama3
        ollama pull gemma2:2b
        ollama pull mistral
        MODEL="llama3"
        ;;
    *)
        echo "Invalid choice, defaulting to gemma2:2b"
        ollama pull gemma2:2b
        MODEL="gemma2:2b"
        ;;
esac

echo ""
echo "=== Testing Model ==="
echo ""

# Quick test
echo "Testing ${MODEL}..."
RESPONSE=$(ollama run ${MODEL} "Say 'Ready to survive!' and nothing else" 2>&1)
echo "Response: ${RESPONSE}"

echo ""
echo "=== Updating Config ==="

# Update the config file with selected model
CONFIG_FILE="./NPCLLMChat/Config/llmconfig.xml"
if [ -f "$CONFIG_FILE" ]; then
    sed -i "s|<Model>.*</Model>|<Model>${MODEL}</Model>|g" "$CONFIG_FILE"
    echo "[✓] Updated config to use ${MODEL}"
else
    echo "[!] Config file not found at ${CONFIG_FILE}"
fi

echo ""
echo "=== Setup Complete ==="
echo ""
echo "To start Ollama server, run:"
echo "  ollama serve"
echo ""
echo "Then start 7 Days to Die and talk to NPCs with @message"
echo ""
echo "Test the connection in-game with:"
echo "  llmchat test"
