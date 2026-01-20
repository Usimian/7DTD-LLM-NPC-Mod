# NPC LLM Chat for 7 Days to Die

Add AI-powered conversations to NPCCore NPCs using a local LLM (Ollama). NPCs can understand requests and take actions like following you, guarding areas, trading, and more!

## Features

- **Natural Conversations**: Talk to NPCs naturally using text or voice
- **Voice Input (STT)**: Push-to-talk with V key
- **Voice Output (TTS)**: NPCs speak their responses
- **Cross-Platform**: Works on Windows 11 and Ubuntu 24
- **Zero External Servers on Windows**: Uses built-in Windows SAPI and Speech Recognition
- **Context Memory**: NPCs remember previous exchanges
- **Dynamic Actions**: NPCs can follow, guard, trade, heal, and more
- **Multiple Personalities**: Each NPC has unique personality traits
- **3D Spatial Audio**: NPC voices positioned in 3D space

## Requirements

- 7 Days to Die (A21+)
- [SCore](https://www.nexusmods.com/7daystodie/mods/2686) or NPCCore
- **Ollama** (local LLM server) - required on all platforms
- **Windows 11**: No additional requirements! Uses built-in TTS/STT
- **Ubuntu 24**: Requires Piper TTS and Whisper STT servers

## Quick Start

### Windows 11 (Easiest!)

```powershell
# 1. Install Ollama from https://ollama.ai/download
# 2. Download a model
ollama pull llama3.2:3b

# 3. Install mod to: 7 Days To Die\Mods\NPCLLMChat\
# 4. Play! Uses Windows built-in TTS/STT automatically
```

See [WINDOWS_INSTALL.md](../WINDOWS_INSTALL.md) for detailed instructions.

### Ubuntu 24

```bash
# 1. Install Ollama
curl -fsSL https://ollama.com/install.sh | sh
ollama pull llama3.2:3b

# 2. Set up TTS server (Piper)
pipx install piper-tts
# Download voices to ~/.local/share/piper/voices/

# 3. Set up STT server (Whisper)
cd whisper-server
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# 4. Start servers
./start_servers.sh

# 5. Build and install mod
cd NPCLLMChat
dotnet build -c Release
# Copy to ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
```

## Platform Support

The mod auto-detects your platform and selects the best TTS/STT provider:

| Platform | TTS (Voice Output) | STT (Voice Input) | External Servers |
|----------|-------------------|-------------------|------------------|
| **Windows 11** | Windows SAPI | Windows Speech Recognition | None needed! |
| **Ubuntu 24** | Piper HTTP Server | Whisper HTTP Server | Required |

You can override this in the config files with `<Provider>Windows</Provider>` or `<Provider>Piper</Provider>`.

## Usage

### Voice Chat
1. Stand near an NPC (within 15 meters)
2. **Hold V key** and speak
3. **Release V key**
4. NPC responds with voice and text

### Text Chat
1. Stand near an NPC (within 5 meters)
2. Open chat (T key)
3. Type `@` followed by your message

```
@Hello there, survivor!
@Come with me, I need backup
@Can you guard this area?
```

### Console Commands (F1)

```
llmchat test            # Test LLM connection
llmchat status          # Show status and stats
llmchat talk <message>  # Talk to nearest NPC
llmchat tts             # Show TTS provider info
llmchat stt             # Show STT provider info
llmchat list            # List active NPCs
llmchat clear           # Clear conversation history
```

## Configuration

### Config Files (in `Config/` folder)

| File | Purpose |
|------|---------|
| `llmconfig.xml` | LLM server, model, action settings |
| `ttsconfig.xml` | TTS provider and voice settings |
| `sttconfig.xml` | STT provider and input settings |
| `personalities.xml` | Custom NPC personalities |

### Provider Selection

In `ttsconfig.xml`:
```xml
<Provider>Auto</Provider>    <!-- Auto-detect by platform (recommended) -->
<Provider>Windows</Provider> <!-- Force Windows SAPI -->
<Provider>Piper</Provider>   <!-- Force Piper server -->
```

In `sttconfig.xml`:
```xml
<Provider>Auto</Provider>    <!-- Auto-detect by platform (recommended) -->
<Provider>Windows</Provider> <!-- Force Windows Speech Recognition -->
<Provider>Whisper</Provider> <!-- Force Whisper server -->
```

### Recommended Models by GPU

| GPU VRAM | Model | Command |
|----------|-------|---------|
| 6-8GB | llama3.2:1b | `ollama pull llama3.2:1b` |
| 8-12GB | llama3.2:3b | `ollama pull llama3.2:3b` |
| 12-16GB | mistral | `ollama pull mistral` |
| 24GB+ | llama3.3:70b | `ollama pull llama3.3:70b` |

## NPC Actions

NPCs can perform these actions based on conversation:

| Action | Example Request | What Happens |
|--------|-----------------|--------------|
| Follow | "Come with me" | NPC follows player |
| Stop | "Stay here" | NPC stops following |
| Wait | "Wait for me" | NPC holds position |
| Guard | "Guard this area" | NPC guards location |
| Trade | "Let's trade" | Opens trade window |

## Building from Source

```bash
cd NPCLLMChat

# Update GamePath in NPCLLMChat.csproj if needed
dotnet build -c Release
```

The DLL is output to `bin/Release/NPCLLMChat.dll`.

## Troubleshooting

### NPC doesn't respond
- Check Ollama: `curl http://localhost:11434/api/tags`
- Use `llmchat test` in console (F1)

### No voice on Windows
- Windows SAPI should work automatically
- Check Settings → Time & Language → Speech
- Use `llmchat tts` to see status

### No voice input on Windows  
- Enable Windows Speech Recognition in Settings
- Check microphone permissions
- Use `llmchat stt` to see status

### Servers not connecting on Linux
```bash
# Check TTS
curl http://localhost:5050/health

# Check STT  
curl http://localhost:5051/health

# Restart servers
./stop_servers.sh
./start_servers.sh
```

## Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                        NPCLLMChat Mod                          │
├────────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌─────────────┐    ┌─────────────────┐    ┌────────────────┐ │
│  │   Ollama    │    │   TTS Service   │    │  STT Service   │ │
│  │   (LLM)     │    │                 │    │                │ │
│  │             │    │  Windows: SAPI  │    │ Windows: SAPI  │ │
│  │  Required   │    │  Linux: Piper   │    │ Linux: Whisper │ │
│  └─────────────┘    └─────────────────┘    └────────────────┘ │
│                                                                │
│  Platform auto-detection selects best provider automatically   │
└────────────────────────────────────────────────────────────────┘
```

## License

MIT - Feel free to modify and share!

## Credits

- SCore/NPCCore team for the NPC framework
- Ollama team for local LLM inference
- Rhasspy/Piper team for neural TTS
- OpenAI Whisper / faster-whisper for speech recognition
- 7DTD Modding community
