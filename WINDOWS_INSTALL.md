# Windows 11 Installation Guide for NPCLLMChat Mod

Complete guide for installing the NPCLLMChat mod on Windows 11, including all required services (LLM, TTS, STT).

## Table of Contents
- [Overview](#overview)
- [System Requirements](#system-requirements)
- [Part 1: Prerequisites](#part-1-prerequisites)
- [Part 2: Install LLM Server (Required)](#part-2-install-llm-server-required)
- [Part 3: Install TTS Server (Optional)](#part-3-install-tts-server-optional)
- [Part 4: Install STT Server (Optional)](#part-4-install-stt-server-optional)
- [Part 5: Build and Install Mod](#part-5-build-and-install-mod)
- [Part 6: Configuration](#part-6-configuration)
- [Part 7: Startup and Testing](#part-7-startup-and-testing)
- [Troubleshooting](#troubleshooting)

## Overview

This mod adds AI-powered conversations to NPCCore/SCore NPCs in 7 Days to Die. The architecture consists of:

1. **NPCLLMChat.dll** - The mod itself (C# Unity mod)
2. **Ollama** - Local LLM server for AI responses (REQUIRED)
3. **Piper Server** - Text-to-speech for NPC voices (Optional)
4. **Whisper Server** - Speech-to-text for voice input (Optional)

All services run locally on your Windows 11 machine - no cloud services required.

## System Requirements

### Minimum
- Windows 11 (64-bit)
- 7 Days to Die (Alpha 21+)
- 16GB RAM
- NVIDIA GPU with 6GB+ VRAM (for LLM)
- 10GB free disk space

### Recommended
- RTX 3080 or better (12GB+ VRAM)
- 32GB+ RAM
- 50GB free disk space (for multiple LLM models)

## Part 1: Prerequisites

### 1.1 Install Python 3.10+

Download from [python.org](https://www.python.org/downloads/windows/)

**IMPORTANT**: During installation, check **"Add Python to PATH"**

Verify installation:
```powershell
python --version
```

Should show: `Python 3.10.x` or higher

### 1.2 Install Git (for cloning repo)

Download from [git-scm.com](https://git-scm.com/download/win)

### 1.3 Install .NET 8 SDK (for building mod)

Download from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0)

Verify:
```powershell
dotnet --version
```

### 1.4 Install Required Game Mods

You MUST have one of these mods installed in 7 Days to Die:
- **SCore** - [Download from Nexus Mods](https://www.nexusmods.com/7daystodie/mods/2686)
- **NPCCore** - Alternative to SCore

These provide the NPC entities that the mod will control.

## Part 2: Install LLM Server (Required)

The LLM server provides the AI brain for NPC conversations.

### 2.1 Install Ollama

1. Download Ollama for Windows: [ollama.ai/download](https://ollama.ai/download)
2. Run the installer
3. Ollama will start automatically and run in the system tray

### 2.2 Download an LLM Model

Open PowerShell or Command Prompt:

**For high-end GPU (RTX 4080/4090, 24GB+ VRAM):**
```powershell
ollama pull llama3.3:70b
```

**For mid-range GPU (RTX 3080/4070, 12-16GB VRAM):**
```powershell
ollama pull llama3.2:3b
```

**For lower-end GPU (8GB VRAM):**
```powershell
ollama pull llama3.2:1b
```

### 2.3 Test Ollama

```powershell
# Check if Ollama is running
curl http://localhost:11434/api/tags
```

Should return a JSON list of installed models.

## Part 3: Install TTS Server (Optional)

Text-to-Speech gives NPCs voice output. Skip this section if you only want text responses.

### 3.1 Install Piper TTS

```powershell
# Install pipx
pip install pipx
pipx ensurepath

# Close and reopen PowerShell, then install Piper
pipx install piper-tts
```

### 3.2 Create Voice Directory

```powershell
# Create directory for voice models
New-Item -Path "$env:USERPROFILE\.local\share\piper\voices" -ItemType Directory -Force
cd "$env:USERPROFILE\.local\share\piper\voices"
```

### 3.3 Download Voice Models

Download these files from [HuggingFace Piper Voices](https://huggingface.co/rhasspy/piper-voices/tree/main/en/en_US):

**Required voices (download both .onnx and .onnx.json files):**

1. **Default voice (Lessac):**
   - `en_US-lessac-medium.onnx`
   - `en_US-lessac-medium.onnx.json`

2. **Companion voice (Amy):**
   - `en_US-amy-medium.onnx`
   - `en_US-amy-medium.onnx.json`

3. **Trader voice (Ryan):**
   - `en_US-ryan-medium.onnx`
   - `en_US-ryan-medium.onnx.json`

Save all files to: `%USERPROFILE%\.local\share\piper\voices\`

### 3.4 Clone Mod Repository for Server Files

```powershell
cd C:\
git clone https://github.com/Usimian/7DTD-LLM-NPC-Mod.git
cd 7DTD-LLM-NPC-Mod
```

### 3.5 Install Piper Server Dependencies

```powershell
cd piper-server
pip install -r requirements.txt
```

### 3.6 Test Piper Server

```powershell
# Start server
python piper_server.py --port 5050

# In another PowerShell window, test:
curl http://localhost:5050/health
```

Should return: `{"piper_available":true,"status":"ok","voices_count":3}`

Press `Ctrl+C` to stop the server for now.

## Part 4: Install STT Server (Optional)

Speech-to-Text allows voice input using push-to-talk (V key). Skip if you only want text chat.

### 4.1 Navigate to Whisper Server Directory

```powershell
cd C:\7DTD-LLM-NPC-Mod\whisper-server
```

### 4.2 Create Virtual Environment

```powershell
python -m venv venv
.\venv\Scripts\activate
```

### 4.3 Install Dependencies

```powershell
pip install -r requirements.txt
```

This will download the Whisper model (1-2GB) on first run.

### 4.4 Test Whisper Server

```powershell
# Start server (preload model)
python whisper_server.py --port 5051 --preload

# In another PowerShell window:
curl http://localhost:5051/health
```

Should return: `{"status":"ok","model_loaded":true}`

## Part 5: Build and Install Mod

### Option A: Download Pre-built Release (Recommended)

1. Go to [GitHub Releases](https://github.com/Usimian/7DTD-LLM-NPC-Mod/releases)
2. Download latest `NPCLLMChat.zip`
3. Extract to your 7 Days to Die Mods folder:
   ```
   %APPDATA%\7DaysToDie\Mods\NPCLLMChat\
   ```

### Option B: Build from Source

```powershell
cd C:\7DTD-LLM-NPC-Mod\NPCLLMChat

# Build the mod
dotnet build -c Release

# Create mod folder
New-Item -Path "$env:APPDATA\7DaysToDie\Mods\NPCLLMChat" -ItemType Directory -Force

# Copy DLL
Copy-Item "bin\Release\NPCLLMChat.dll" "$env:APPDATA\7DaysToDie\Mods\NPCLLMChat\"

# Copy config files
Copy-Item "ModInfo.xml" "$env:APPDATA\7DaysToDie\Mods\NPCLLMChat\"
Copy-Item -Recurse "Config" "$env:APPDATA\7DaysToDie\Mods\NPCLLMChat\"
Copy-Item -Recurse "XUi" "$env:APPDATA\7DaysToDie\Mods\NPCLLMChat\"
```

### Verify Installation

Check that these files exist:
```
%APPDATA%\7DaysToDie\Mods\NPCLLMChat\
├── NPCLLMChat.dll
├── ModInfo.xml
├── Config\
│   ├── llmconfig.xml
│   ├── ttsconfig.xml
│   ├── sttconfig.xml
│   └── personalities.xml
└── XUi\
    └── windows.xml
```

## Part 6: Configuration

### 6.1 Configure LLM (Required)

Edit: `%APPDATA%\7DaysToDie\Mods\NPCLLMChat\Config\llmconfig.xml`

```xml
<LLMConfig>
    <Server>
        <Endpoint>http://localhost:11434/api/generate</Endpoint>

        <!-- Change this to match the model you downloaded -->
        <Model>llama3.2:3b</Model>

        <!-- GPU Layers: -1 = auto, or specify manually -->
        <NumGPULayers>-1</NumGPULayers>

        <!-- Context window size -->
        <NumCtx>8192</NumCtx>
    </Server>

    <Generation>
        <Temperature>0.8</Temperature>
        <MaxTokens>200</MaxTokens>
    </Generation>
</LLMConfig>
```

### 6.2 Configure TTS (Optional)

Edit: `%APPDATA%\7DaysToDie\Mods\NPCLLMChat\Config\ttsconfig.xml`

```xml
<TTSConfig>
    <Server>
        <Enabled>true</Enabled>
        <Endpoint>http://localhost:5050/synthesize</Endpoint>
    </Server>

    <Audio>
        <Volume>0.8</Volume>
        <SpeechRate>1.0</SpeechRate>
        <MaxDistance>20</MaxDistance>
        <MinDistance>2</MinDistance>
    </Audio>

    <Voices>
        <DefaultVoice>en_US-lessac-medium</DefaultVoice>
        <CompanionVoice>en_US-amy-medium</CompanionVoice>
        <TraderVoice>en_US-ryan-medium</TraderVoice>
    </Voices>
</TTSConfig>
```

### 6.3 Configure STT (Optional)

Edit: `%APPDATA%\7DaysToDie\Mods\NPCLLMChat\Config\sttconfig.xml`

```xml
<STTConfig>
    <Server>
        <Enabled>true</Enabled>
        <Endpoint>http://localhost:5051/transcribe</Endpoint>
    </Server>

    <Audio>
        <SampleRate>16000</SampleRate>
        <MaxRecordingSeconds>15</MaxRecordingSeconds>
    </Audio>

    <Input>
        <PushToTalkKey>V</PushToTalkKey>
    </Input>

    <Whisper>
        <Model>base.en</Model>
        <Language>en</Language>
    </Whisper>
</STTConfig>
```

## Part 7: Startup and Testing

### 7.1 Create Startup Scripts

For convenience, create batch files to start the servers.

**start-ollama.bat** (usually auto-starts, but just in case):
```batch
@echo off
echo Ollama should already be running in system tray
echo If not, run: ollama serve
pause
```

**start-tts.bat:**
```batch
@echo off
cd /d C:\7DTD-LLM-NPC-Mod\piper-server
echo Starting Piper TTS Server on port 5050...
python piper_server.py --port 5050
pause
```

**start-stt.bat:**
```batch
@echo off
cd /d C:\7DTD-LLM-NPC-Mod\whisper-server
call venv\Scripts\activate
echo Starting Whisper STT Server on port 5051...
python whisper_server.py --port 5051 --preload
pause
```

### 7.2 Startup Sequence

1. **Verify Ollama is running** (check system tray for Ollama icon)
2. **Start TTS server** (if using voice output): Run `start-tts.bat`
3. **Start STT server** (if using voice input): Run `start-stt.bat`
4. **Launch 7 Days to Die**

### 7.3 In-Game Testing

1. Start a game with NPCCore/SCore NPCs
2. Find an NPC
3. Open chat and type: `@Hello there`
4. The NPC should respond with AI-generated text
5. If TTS is enabled, you'll hear the NPC speak

### 7.4 In-Game Configuration UI

Press `ESC` → Click **"NPC AI Chat Settings"** button

Here you can:
- Test LLM connection
- Test TTS (hear voice samples)
- Test STT (record and transcribe)
- Change AI model
- Adjust volume and speech rate
- Select voices for different NPC types
- Enable/disable TTS and STT

### 7.5 Voice Input Testing

1. Stand near an NPC
2. Hold `V` key (or configured PTT key)
3. Speak: "Hello, can you follow me?"
4. Release `V` key
5. NPC should transcribe your speech and respond

## Troubleshooting

### NPC doesn't respond to chat

**Check Ollama:**
```powershell
# Test endpoint
curl http://localhost:11434/api/tags

# If not working, restart Ollama
taskkill /F /IM ollama.exe
ollama serve
```

**Check game logs:**
- Location: `%APPDATA%\7DaysToDie\logs\output_log.txt`
- Search for: `[NPCLLMChat]`

### No voice output (TTS)

1. **Check Piper server is running:**
   ```powershell
   curl http://localhost:5050/health
   ```

2. **Verify voice models exist:**
   ```powershell
   dir "$env:USERPROFILE\.local\share\piper\voices"
   ```

3. **Check ttsconfig.xml:**
   - `<Enabled>true</Enabled>`
   - Correct voice names

4. **Test in-game:**
   - ESC → NPC AI Chat Settings → Test TTS

### Voice input not working (STT)

1. **Check Whisper server:**
   ```powershell
   curl http://localhost:5051/health
   ```

2. **Verify microphone permissions:**
   - Windows Settings → Privacy → Microphone
   - Allow desktop apps to access microphone

3. **Test in console:**
   - Press `F1` in-game
   - Type: `llmchat stt test`
   - Hold V and speak

### "Model not found" error

The model name in `llmconfig.xml` must exactly match an installed model:

```powershell
# List installed models
ollama list

# Pull missing model
ollama pull llama3.2:3b
```

### Firewall blocking connections

Add firewall exceptions:
```powershell
# Allow Python
netsh advfirewall firewall add rule name="Python for 7DTD Mod" dir=in action=allow program="%LOCALAPPDATA%\Programs\Python\Python310\python.exe" enable=yes

# Allow Ollama
netsh advfirewall firewall add rule name="Ollama" dir=in action=allow program="%LOCALAPPDATA%\Programs\Ollama\ollama.exe" enable=yes
```

### Performance issues

**For slower GPUs:**
1. Use smaller model: `llama3.2:1b`
2. Reduce context: `<NumCtx>2048</NumCtx>`
3. Lower GPU layers: `<NumGPULayers>20</NumGPULayers>`

**For faster responses:**
1. Use faster model: `gemma2:2b`
2. Reduce max tokens: `<MaxTokens>100</MaxTokens>`

### Building errors

**"Assembly not found" errors:**
- Update the `<GamePath>` in `NPCLLMChat.csproj` to your 7DTD installation path
- Example: `C:\Program Files (x86)\Steam\steamapps\common\7 Days to Die`

**Missing .NET SDK:**
```powershell
dotnet --version
```
Should show 8.0 or higher. Reinstall from [dotnet.microsoft.com](https://dotnet.microsoft.com)

## Performance Expectations

### With RTX 4080/4090:
- LLM Response: 1-3 seconds (llama3.3:70b)
- TTS Synthesis: < 500ms
- STT Transcription: 1-2 seconds
- Total voice-to-voice: 3-6 seconds

### With RTX 3060/3070:
- Use llama3.2:3b
- LLM Response: 2-5 seconds
- TTS/STT: Same as above

## File Locations Reference

### Game Mods
```
%APPDATA%\7DaysToDie\Mods\NPCLLMChat\
```

### Voice Models
```
%USERPROFILE%\.local\share\piper\voices\
```

### Server Scripts
```
C:\7DTD-LLM-NPC-Mod\piper-server\
C:\7DTD-LLM-NPC-Mod\whisper-server\
```

### Game Logs
```
%APPDATA%\7DaysToDie\logs\output_log.txt
```

## Console Commands

Press `F1` in-game to access console:

```
llmchat test          - Test LLM connection
llmchat status        - Show performance stats
llmchat talk <msg>    - Talk to nearest NPC
llmchat tts           - Show TTS status
llmchat tts test      - Test TTS audio
llmchat stt           - Show STT status
llmchat stt test      - Test voice recording
llmchat list          - List active NPCs
llmchat clear         - Clear conversation history
```

## Getting Help

- **GitHub Issues**: [Report bugs](https://github.com/Usimian/7DTD-LLM-NPC-Mod/issues)
- **Discord**: Join the 7DTD modding community
- **Check logs**: `%APPDATA%\7DaysToDie\logs\output_log.txt`

## What's Different from Linux

If you've used Linux guides, here are the key Windows differences:

1. **Paths**: Use `%APPDATA%` instead of `~/.local/share/`
2. **Python virtual env**: `venv\Scripts\activate` instead of `source venv/bin/activate`
3. **Piper installation**: Uses `pipx` instead of binary download
4. **No chmod needed**: Windows handles permissions differently
5. **Backslashes**: Use `\` in paths, not `/`

## Summary

You now have:
- ✅ Local LLM running (Ollama)
- ✅ TTS server for NPC voices (Piper)
- ✅ STT server for voice input (Whisper)
- ✅ Mod installed in 7DTD
- ✅ All services communicating via HTTP

Talk to NPCs naturally in 7 Days to Die with AI-powered conversations!
