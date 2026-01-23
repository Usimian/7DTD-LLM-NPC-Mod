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

1. **Download** from GitHub:
   - Download `7DTD-LLM-NPC-Mod-master.zip` from the repository
   - Or click: Code → Download ZIP

2. **Extract** the downloaded file:
   - Extract `7DTD-LLM-NPC-Mod-master.zip` to a temporary location
   - Navigate **inside** the extracted folder
   - You will see the NPCLLMChat, piper-server andwhisper-server folders

3. **Run the installer**:
   - Open Command Prompt or PowerShell in the extracted folder
   - Run the install script with your game path:
   
   **If using Command Prompt (cmd):**
   ```
   install.bat "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
   ```
   
   **If using PowerShell:**
   ```
   .\install.bat "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
   ```
   
   - Replace the path with your actual game installation directory
   - Common locations:
     - Steam: `C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die`
     - Epic: `C:\Program Files\Epic Games\7DaysToDie`

   The installer will automatically copy all files to `<Game>\Mods\NPCLLMChat\`

4. **Run setup** (one time only):
   - The installer will show you the next steps
   - Navigate to the mod folder and run:
   ```bash
   cd "<Game>\Mods\NPCLLMChat"
   setup_servers.bat
   ```
   - This installs Python dependencies for voice features (~2-3 minutes)

5. **Download AI model**:
   ```bash
   ollama pull gemma3:4b
   ```
   - Downloads ~2.5 GB AI model (few minutes depending on connection)

6. **Launch game** - Everything auto-starts!

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
- `gemma2:9b` - Better quality, larger
- `llama3.2:3b` - Faster, good balance
- `mistral:7b` - High quality
- `phi3:mini` - Very fast, small

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

**Main Commands:**
- `llmchat status` - Show mod status and performance
- `llmchat test` - Test LLM connection
- `llmchat talk <message>` - Talk to nearest NPC
- `llmchat action <action>` - Execute NPC action (follow, stop, guard, wait)
- `llmchat clear` - Clear all conversation history
- `llmchat list` - List active NPC sessions

**TTS Commands:**
- `llmchat tts` - Show TTS status
- `llmchat tts test` - Test voice synthesis
- `llmchat tts on/off` - Enable/disable TTS
- `llmchat tts voices` - List available voices

**STT Commands:**
- `llmchat stt` - Show STT status
- `llmchat stt test` - Test microphone recording
- `llmchat stt on/off` - Enable/disable voice input
- `llmchat stt devices` - List available microphones

## ❓ Troubleshooting

### NPC doesn't respond
- Check Ollama: `ollama list` in cmd
- Verify model installed: `ollama pull gemma3:4b`
- Use @ symbol: `@Hello` not just `Hello`
- Check in-game: `llmchat status` and `llmchat test`

### Voice chat not working
- Run `setup_servers.bat` if not already done
- Check Python: `python --version`
- In-game: `llmchat stt status` and `llmchat stt devices`
- Try `llmchat stt test` to verify microphone

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
