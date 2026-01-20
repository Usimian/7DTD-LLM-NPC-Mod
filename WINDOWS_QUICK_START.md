# Windows Quick Start Guide

## Prerequisites

✅ **Already Installed:**
- Ollama (running as Windows service with `gemma3:4b` model)
- Python 3.12
- 7 Days to Die (Steam)

## Quick Start (3 Steps)

### 1. Start All Servers

Double-click `start_all_servers.bat` in the mod folder.

This will:
- ✅ Check Ollama is running
- ✅ Start Whisper STT server (port 5051)
- ✅ Start Piper TTS server (port 5050)

**Wait for "All servers started!" message** (~10 seconds)

### 2. Launch 7 Days to Die

Start the game normally through Steam.

### 3. Test the Mod

**In-game:**

1. **Text Chat** (works immediately):
   - Press `T` to open chat
   - Type `@Hello there!`
   - NPC responds via text

2. **Voice Input** (requires Whisper running):
   - Press and hold `V`
   - Speak: "What do you need?"
   - Release `V`
   - NPC responds via text

3. **Voice Output** (requires Piper running):
   - Configured in-game via `F1` console
   - Type: `llmchat tts on`
   - NPCs will speak responses

---

## Troubleshooting

### "LLM request failed: 404"

Ollama model not loaded. Run:
```batch
ollama run gemma3:4b "test"
```

Then restart the game.

### "Whisper server not available"

Start the servers first:
```batch
start_all_servers.bat
```

Wait for "All servers started!", then launch the game.

### "Piper TTS server not available"

First time setup:
```batch
pip install piper-tts flask numpy
```

Then download a voice model:
```batch
mkdir "%USERPROFILE%\AppData\Local\piper\voices"
REM Download from https://huggingface.co/rhasspy/piper-voices
```

---

## Stop Servers

Double-click `stop_all_servers.bat`

---

## Console Commands (F1 in-game)

```
llmchat status          - Show all service status
llmchat tts on/off      - Enable/disable voice output
llmchat stt on/off      - Enable/disable voice input
llmchat test_tts        - Test voice synthesis
```

---

## What's Running?

| Service | Port | Purpose |
|---------|------|---------|
| **Ollama** | 11434 | AI brain (LLM responses) |
| **Whisper** | 5051 | Voice → Text (STT) |
| **Piper** | 5050 | Text → Voice (TTS) |

---

## Performance Tips

- **Text-only mode**: Don't run Whisper/Piper servers - just use `@` chat
- **Fast responses**: Keep `gemma3:4b` model (1-2 second responses)
- **Better quality**: Upgrade to `llama3:70b` if you have 48GB+ VRAM
