# 7 Days to Die - LLM NPC Chat Mod

Transform your 7 Days to Die NPCs into intelligent, voice-enabled companions powered by AI! This mod adds realistic conversations, voice chat, and dynamic NPC actions using local LLM technology.

[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%7C%20Ubuntu%2024-blue)]()
[![7DTD](https://img.shields.io/badge/7%20Days%20to%20Die-A21%2B-green)]()
[![License](https://img.shields.io/badge/license-MIT-orange)]()

## ✨ Features

### 🤖 **AI-Powered Conversations**
- Natural, context-aware dialogue with NPCs
- Persistent conversation history
- Personality-driven responses
- Multiple NPC types (traders, companions, bandits)

### 🎤 **Voice Input (Speech-to-Text)**
- Push-to-talk voice chat (`V` key by default)
- Powered by Whisper AI for accurate transcription
- Automatic microphone detection

### 🔊 **Voice Output (Text-to-Speech)**
- NPCs speak their responses with natural voices
- Multiple voice options per NPC type
- Powered by Piper TTS for high-quality speech
- Adjustable volume and speech rate

### 🎮 **NPC Actions**
- NPCs can follow, guard, wait, and trade
- Context-aware action execution
- Natural language commands ("Come with me!", "Guard this area")

### 🚀 **Seamless Experience (Windows)**
- **Zero manual setup** - servers auto-start with the game
- Everything runs in the background
- Auto-cleanup on game exit

## 📋 Requirements

### All Platforms
- **7 Days to Die** (Alpha 21+)
- **[Ollama](https://ollama.ai)** - Local LLM server (runs the AI brain)
- **Python 3.10+** - For TTS/STT servers

### Windows Additional Requirements
- **.NET SDK 6.0+** (for building from source)

### Ubuntu Additional Requirements
- **Build tools**: `sudo apt install build-essential`

## 🎯 Quick Start

### Windows 11

#### 1. Install Ollama
```batch
# Download and install from https://ollama.ai
# Or use winget:
winget install Ollama.Ollama

# Pull an AI model (recommended: gemma3:4b for fast responses)
ollama pull gemma3:4b
```

#### 2. Install Python Dependencies
```batch
# Install piper-tts globally
pip install piper-tts flask numpy

# Install whisper in the mod's venv (done automatically by install script)
```

#### 3. Install the Mod
```batch
# Clone this repository
git clone https://github.com/Usimian/7DTD-LLM-NPC-Mod.git
cd 7DTD-LLM-NPC-Mod

# Run the Windows installer
install_mod_windows.bat
```

#### 4. Download a Voice (First Time Only)
```batch
# Run this once to download a TTS voice model (~60MB)
setup_piper_voice.bat
```

#### 5. Play!
Just launch 7 Days to Die normally. The mod will:
- ✅ Auto-start Piper TTS server
- ✅ Auto-start Whisper STT server  
- ✅ Connect to Ollama (already running as Windows service)

**That's it!** Everything runs automatically in the background.

---

### Ubuntu 24

#### 1. Install Ollama
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull an AI model
ollama pull gemma3:4b

# Start Ollama service
sudo systemctl enable ollama
sudo systemctl start ollama
```

#### 2. Set Up Python Servers
```bash
# Install Piper TTS
pip install piper-tts flask numpy

# Set up Whisper STT
cd whisper-server
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
deactivate
cd ..
```

#### 3. Install the Mod
```bash
# Clone the repo
git clone https://github.com/Usimian/7DTD-LLM-NPC-Mod.git
cd 7DTD-LLM-NPC-Mod

# Build the mod
cd NPCLLMChat
dotnet build -c Release

# Copy to your 7DTD Mods folder
cp -r bin/Release/* ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
cp -r Config ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
cp ModInfo.xml ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
```

#### 4. Start Servers (Each Game Session)
```bash
# Terminal 1 - Piper TTS
./start_tts_server.sh

# Terminal 2 - Whisper STT  
./start_stt_server.sh
```

#### 5. Play!
Launch 7 Days to Die and enjoy AI-powered NPCs!

---

## 🎮 In-Game Usage

### Text Chat
Press `T` to open chat, then:
```
@Hello there!
@What supplies do you have?
@Can you come with me?
```

The `@` prefix sends messages to the nearest NPC.

### Voice Chat
1. **Hold `V`** to record
2. **Speak** your message
3. **Release `V`** 
4. NPC responds with voice!

### Console Commands
Press `F1` to open console:

```bash
# Status
llmchat status              # Show all services status

# TTS (Text-to-Speech)
llmchat tts on              # Enable voice output
llmchat tts off             # Disable voice output
llmchat tts test            # Test TTS with sample audio
llmchat tts voices          # List available voices

# STT (Speech-to-Text)
llmchat stt on              # Enable voice input
llmchat stt off             # Disable voice input
llmchat stt test            # Test microphone recording
llmchat stt refresh         # Reconnect to Whisper server
llmchat stt devices         # List available microphones

# Configuration
llmchat config              # Open in-game config UI
```

## ⚙️ Configuration

### In-Game Config UI
Press `ESC` → `Mod Settings` → `NPCLLMChat`

Configure:
- AI model selection
- Voice settings
- Microphone input
- NPC personalities

### Config Files
Located in `Mods/NPCLLMChat/Config/`:

- **`llmconfig.xml`** - LLM/AI settings (model, temperature, context)
- **`ttsconfig.xml`** - Text-to-speech settings (voices, volume)
- **`sttconfig.xml`** - Speech-to-text settings (microphone, sensitivity)
- **`personalities.xml`** - NPC personality templates

## 🎭 Recommended AI Models

| Model | VRAM | Response Time | Quality | Best For |
|-------|------|---------------|---------|----------|
| `gemma3:4b` | 4GB | < 1s | Good | Fast gameplay, low-end GPUs |
| `llama3:8b` | 8GB | 1-2s | Great | Balanced performance |
| `mixtral:8x7b` | 24GB | 2-3s | Excellent | High-end GPUs, best dialogue |
| `llama3:70b` | 48GB+ | 3-5s | Best | RTX 6000/A100, max immersion |

**Default:** `gemma3:4b` (fast and works on most systems)

## 🔧 Troubleshooting

### "LLM not available"
```bash
# Check Ollama is running
ollama ps

# If no model loaded, load one:
ollama run gemma3:4b "test"
```

### "TTS server not available"
```bash
# Windows: Auto-starts, wait 5-10 seconds after launching game
# If still failing, manually test:
python piper_server.py --port 5050

# Ubuntu: Start manually
./start_tts_server.sh
```

### "STT server not available"
```bash
# In-game, press F1 and type:
llmchat stt refresh

# Or restart the server manually
# Windows: Restart game (auto-starts fresh)
# Ubuntu: ./start_stt_server.sh
```

### Voice Model Not Found
```bash
# Windows
setup_piper_voice.bat

# Ubuntu/Linux
cd ~/.local/share/piper/voices
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json
```

### Performance Issues
- Use a smaller model: `gemma3:4b` or `llama3:8b`
- Reduce context window in `llmconfig.xml` (`<NumCtx>2048</NumCtx>`)
- Disable TTS if you only want text: `llmchat tts off`

## 📁 Project Structure

```
7DTD-LLM-NPC-Mod/
├── NPCLLMChat/              # Main mod source
│   ├── Scripts/             # C# mod code
│   ├── Config/              # Configuration files
│   └── bin/Release/         # Compiled DLL
├── piper-server/            # TTS server (Python)
├── whisper-server/          # STT server (Python)
├── install_mod_windows.bat  # Windows installer
└── README.md
```

## 🤝 Contributing

Contributions welcome! Please:
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Submit a pull request

## 📝 License

MIT License - see LICENSE file for details

## 🙏 Credits

- **AI Models**: [Ollama](https://ollama.ai)
- **TTS**: [Piper](https://github.com/rhasspy/piper)
- **STT**: [Faster Whisper](https://github.com/guillaumekln/faster-whisper)
- **Game**: [7 Days to Die](https://7daystodie.com) by The Fun Pimps

## 🐛 Known Issues

- First voice response after game start may have 5-10 second delay (servers warming up)
- Some special characters in NPC dialogue may display incorrectly
- Action system requires SCore mod for some NPCs

## 🗺️ Roadmap

- [ ] Multiplayer support
- [ ] More voice options (accents, languages)
- [ ] Dynamic personality learning
- [ ] Quest generation
- [ ] Faction relationships

---

**Made with ❤️ for the 7 Days to Die modding community**

*Questions? Open an issue on GitHub!*
