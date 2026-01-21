# NPCLLMChat - AI-Powered NPC Conversations

Talk to NPCs in 7 Days to Die using AI! Voice or text chat with NPCs that remember your conversations.

## ✨ Features

- 🗣️ **Voice Chat** - Hold V to talk to NPCs
- 💬 **Text Chat** - Type `@message` in chat
- 🧠 **Memory** - NPCs remember your conversations
- 🎭 **Personality System** - Different NPC personalities
- 🔊 **Natural Voice** - NPCs respond with speech
- 🌐 **Fully Local** - No internet needed after setup

## 📦 Installation

### Prerequisites

1. **Required Mods** (install first):
   - [0-SCore](https://www.nexusmods.com/7daystodie/mods/6176) - Must match game version (e.g., SCore 2.5.x for game v2.5)
   - [0-NPCCore](https://www.nexusmods.com/7daystodie/mods/8099) - Provides NPCs

2. **Required Software**:
   - [Ollama](https://ollama.com/download) - AI language model
   - [Python 3.9+](https://www.python.org/downloads/) - Check "Add to PATH" during install

### Install Steps

1. **Extract** the mod to your game's Mods folder:
   ```
   <Game>\Mods\NPCLLMChat\
   ```

2. **Run setup** (one time only):
   ```bash
   cd Mods\NPCLLMChat
   setup_servers.bat
   ```

3. **Download AI model**:
   ```bash
   ollama pull gemma3:4b
   ```

4. **Launch game** - Everything auto-starts!

## 🎮 Usage

**Text Chat**: `@Hello there!`  
**Voice Chat**: Hold **V** key, speak, release

NPCs will respond with voice and text!

## ⚙️ Configuration

Edit files in `Mods\NPCLLMChat\Config\`:

### Change AI Model (`llmconfig.xml`)
```xml
<Model>gemma3:4b</Model>
```

Other models:
- `gemma2:9b` - Better quality
- `llama3.2:3b` - Faster
- `mistral:7b` - Balanced

### Adjust Voice (`ttsconfig.xml`)
```xml
<Volume>0.8</Volume>          <!-- 0.0 to 1.0 -->
<SpeechRate>1.0</SpeechRate>  <!-- 0.5 to 2.0 -->
```

### Push-to-Talk Key (`sttconfig.xml`)
```xml
<PushToTalkKey>V</PushToTalkKey>  <!-- V, LeftAlt, Mouse3, etc -->
```

## 🔧 Console Commands

Press **F1** in-game:

- `llmchat status` - Show mod status
- `llmchat test` - Test nearest NPC
- `llmchat tts test` - Test voice
- `llmchat stt test` - Test microphone
- `help llmchat` - Show all commands

## ❓ Troubleshooting

### NPC doesn't respond
- Check Ollama: `ollama list` in cmd
- Verify model installed: `ollama pull gemma3:4b`
- Use @ symbol: `@Hello` not just `Hello`

### Voice chat not working
- Run `setup_servers.bat`
- Check Python: `python --version`
- In-game: `llmchat stt status`

### No voice from NPC
- In-game: `llmchat tts status`
- Check volume in ttsconfig.xml

## 📋 System Requirements

**Minimum**:
- 8 GB RAM
- 5 GB disk space
- Windows 10/11

**Recommended**:
- 16 GB RAM
- NVIDIA GPU with 4GB+ VRAM
- SSD

## 🏗️ For Developers

### Building from Source

1. Clone the repo
2. Set game path in `NPCLLMChat.csproj`:
   ```xml
   <GamePath>C:\...\7 Days To Die</GamePath>
   ```
3. Build:
   ```bash
   dotnet build NPCLLMChat\NPCLLMChat.csproj -c Release
   ```

### Packaging for Release

```bash
package_release.bat
```

Creates `NPCLLMChat-v1.0.0.zip` ready for distribution.

## 📜 Credits

- **sphereii** - 0-SCore and 0-NPCCore frameworks
- **Ollama** - Local LLM inference
- **Piper TTS** - Neural text-to-speech
- **Faster Whisper** - Speech recognition

## 📄 License

MIT License - See LICENSE file

## 🐛 Issues

Report bugs at: https://github.com/Usimian/7DTD-LLM-NPC-Mod/issues

Include:
- Game version
- Error messages
- Log files from `%APPDATA%\7DaysToDie\logs\`

---

Enjoy natural conversations with NPCs! 🎮🤖
