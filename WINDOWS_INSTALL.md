# Windows 11 Installation Guide for NPCLLMChat Mod

## 🚀 Quick Start (5 Minutes)

**The mod works out of the box on Windows 11 - no Python servers needed!**

### Step 1: Install Ollama (Required)

1. Download Ollama: [ollama.ai/download](https://ollama.ai/download)
2. Run the installer
3. Open PowerShell and download a model:

```powershell
ollama pull llama3.2:3b
```

Ollama auto-starts with Windows - that's all you need!

### Step 2: Install the Mod

1. Download `NPCLLMChat.zip` from [Releases](https://github.com/Usimian/7DTD-LLM-NPC-Mod/releases)
2. Extract to your 7DTD Mods folder:
   ```
   C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die\Mods\NPCLLMChat\
   ```

### Step 3: Play!

1. Launch 7 Days to Die (requires SCore or NPCCore mod for NPCs)
2. Find an NPC
3. Type `@Hello there!` in chat, or hold **V** to voice chat

**Done!** The mod automatically uses:
- **Windows SAPI** for NPC voices (built into Windows)
- **Windows Speech Recognition** for voice input (built into Windows)

---

## How It Works

The mod auto-detects your platform:

| Platform | TTS (Voice Output) | STT (Voice Input) |
|----------|-------------------|-------------------|
| **Windows 11** | Windows SAPI (built-in) | Windows Speech Recognition |
| **Ubuntu 24** | Piper Server | Whisper Server |

No configuration needed - just install and play!

---

## Configuration

### Changing the AI Model

Edit `Config\llmconfig.xml`:

```xml
<Model>llama3.2:3b</Model>  <!-- Match your installed model -->
```

**Recommended models by GPU:**
| GPU VRAM | Model | Command |
|----------|-------|---------|
| 6-8GB | llama3.2:1b | `ollama pull llama3.2:1b` |
| 8-12GB | llama3.2:3b | `ollama pull llama3.2:3b` |
| 12-16GB | mistral | `ollama pull mistral` |
| 24GB+ | llama3.3:70b | `ollama pull llama3.3:70b` |

### Force a Specific Provider

In `Config\ttsconfig.xml` or `Config\sttconfig.xml`:

```xml
<Provider>Auto</Provider>    <!-- Auto-detect (recommended) -->
<Provider>Windows</Provider> <!-- Force Windows built-in -->
<Provider>Piper</Provider>   <!-- Force Piper/Whisper server -->
```

---

## Troubleshooting

### No voice output?

Windows SAPI should work automatically. Check:
- Windows Settings → Time & Language → Speech
- Ensure English voices are installed

### Voice input not working?

1. Enable Windows Speech Recognition:
   - Settings → Time & Language → Speech
2. Check microphone permissions:
   - Settings → Privacy → Microphone

### NPC doesn't respond?

Check Ollama is running:
```powershell
curl http://localhost:11434/api/tags
```

### Check logs

Game logs: `%APPDATA%\7DaysToDie\logs\output_log.txt`

Search for `[NPCLLMChat]`

---

## Building from Source

```powershell
cd NPCLLMChat
dotnet build -c Release
```

The DLL will be in `bin\Release\NPCLLMChat.dll`

---

## Console Commands

Press **F1** in-game:

```
llmchat test      - Test LLM connection
llmchat status    - Show provider status
llmchat tts       - Show TTS info
llmchat stt       - Show STT info  
llmchat talk <msg> - Talk to nearest NPC
```
