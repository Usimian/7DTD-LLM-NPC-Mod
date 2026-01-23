# NPCLLMChat - AI-Powered NPC Conversations

Talk to NPCs in 7 Days to Die using AI! Voice or text chat with NPCs that remember your conversations.

## Installation

### Prerequisites

Install these first:
- [0-SCore](https://www.nexusmods.com/7daystodie/mods/6176) - Must match game version
- [0-NPCCore](https://www.nexusmods.com/7daystodie/mods/8099) - Provides NPCs
- [Ollama](https://ollama.com/download) - AI language model
- [Python 3.9+](https://www.python.org/downloads/) - Check "Add to PATH" during install

### Install Steps

1. **Download** [`NPCLLMChat-v1.0.0.zip`](https://github.com/Usimian/7DTD-LLM-NPC-Mod/raw/master/NPCLLMChat-v1.0.0.zip)

2. **Extract** the `NPCLLMChat` folder to your game's Mods directory:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\NPCLLMChat\
   ```

3. **Run setup** (one time):
   ```
   cd "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\NPCLLMChat"
   setup_servers.bat
   ```

4. **Download AI model**:
   ```
   ollama pull gemma3:4b
   ```

5. **Launch game** - Everything auto-starts!

### Usage

- **Text Chat**: Type `@Hello there!` in chat
- **Voice Chat**: Hold **V** key, speak, release

---

## For Developers

### NPC Prompt Configuration

The NPC system prompt and personalities are stored in XML config files:

**Base System Prompt** (`NPCLLMChat/Config/llmconfig.xml`):
```xml
<Personality>
    <SystemPrompt>You are a survivor in a post-apocalyptic zombie wasteland...</SystemPrompt>
</Personality>
```

**Individual Personalities** (`NPCLLMChat/Config/personalities.xml`):
```xml
<Personality id="trader_gruff">
    <Name>Gruff Trader</Name>
    <Traits>You're a no-nonsense trader who's seen too much...</Traits>
</Personality>
```

### Building from Source

1. Clone the repo
2. Set game path in `NPCLLMChat/NPCLLMChat.csproj`:
   ```xml
   <GamePath>C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die</GamePath>
   ```
3. Build:
   ```
   dotnet build NPCLLMChat\NPCLLMChat.csproj -c Release
   ```

**Note**: If you change the XML config files, no rebuild is needed - just restart the game. Only C# code changes require rebuilding.

### Creating a Release Package

```
package_release.bat
```

Creates `NPCLLMChat-v1.0.0.zip` for distribution. Upload to GitHub Releases.

---

## Troubleshooting

- **NPC doesn't respond**: Check `ollama list` in cmd, verify model is installed
- **Voice not working**: Run `setup_servers.bat`, check `python --version`
- **In-game diagnostics**: Press F1, type `llmchat status`

## Credits

- **sphereii** - 0-SCore and 0-NPCCore frameworks
- **Ollama** - Local LLM inference
- **Piper TTS** - Neural text-to-speech
- **Faster Whisper** - Speech recognition

## License

MIT License
