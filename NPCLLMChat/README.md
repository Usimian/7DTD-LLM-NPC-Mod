# NPC LLM Chat for 7 Days to Die

Add AI-powered conversations to NPCCore NPCs using a local LLM (Ollama, LM Studio, etc.). NPCs can understand requests and take actions like following you, guarding areas, trading, and more!

## Features

- **Natural Conversations**: Talk to NPCs naturally using text or voice
- **Voice Input (STT)**: Push-to-talk with V key using Whisper speech recognition
- **Voice Output (TTS)**: NPCs speak their responses with Piper text-to-speech
- **Context Memory**: NPCs remember previous exchanges
- **Dynamic Actions**: NPCs can follow, guard, trade, heal, and more based on conversation
- **Multiple Personalities**: Each NPC has unique personality traits
- **3D Spatial Audio**: NPC voices positioned in 3D space
- **GPU Optimized**: Configured for high-end GPUs like RTX 6000 Pro
- **Performance Tracking**: Built-in benchmarking and response time monitoring

## Requirements

- 7 Days to Die (A21+)
- [NPCCore](https://www.nexusmods.com/7daystodie/mods/123) / SCore
- Local LLM server (Ollama recommended)
- [Harmony](https://github.com/pardeike/Harmony) (usually included with SCore)
- .NET 4.8 SDK (for building)
- **Python 3.8+** (for TTS/STT servers)
- **Piper TTS** (for voice output)
- **Whisper** (for voice input)

## Installation

### 1. Set Up Ollama with GPU Support

```bash
# Install Ollama (Linux)
curl -fsSL https://ollama.com/install.sh | sh

# Pull a model (for RTX 6000 Pro with 48GB VRAM, use the full 70B model!)
ollama pull llama3.3:70b

# Or for faster responses:
ollama pull llama3.2:3b

# Start Ollama server
ollama serve
```

### 2. Set Up TTS (Text-to-Speech)

```bash
# Install Piper TTS
pipx install piper-tts

# Download voices
mkdir -p ~/.local/share/piper/voices
cd ~/.local/share/piper/voices

# Download a voice (example: lessac medium)
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json

# More voices available at: https://huggingface.co/rhasspy/piper-voices
```

### 3. Set Up STT (Speech-to-Text)

```bash
# Navigate to whisper server directory
cd /path/to/7DTD-LLM-NPC-Mod/whisper-server

# Create virtual environment and install dependencies
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

### 4. Build the Mod

**Option A: Using .NET CLI**
```bash
cd /home/marc/7DTD-LLM-NPC-Mod/NPCLLMChat

# Update GamePath in .csproj to your 7DTD location first!
dotnet build -c Release
```

**Option B: Using Visual Studio / Rider**
1. Open `NPCLLMChat.sln`
2. Update assembly references if paths differ
3. Build → Release

### 5. Install the Mod

Copy the built `NPCLLMChat.dll` and configuration files to your 7DTD Mods folder:
```bash
# Create mod folder
mkdir -p ~/.local/share/7DaysToDie/Mods/NPCLLMChat/Config

# Copy files
cp bin/Release/NPCLLMChat.dll ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
cp ModInfo.xml ~/.local/share/7DaysToDie/Mods/NPCLLMChat/
cp Config/*.xml ~/.local/share/7DaysToDie/Mods/NPCLLMChat/Config/
```

## Usage

### Starting Servers

Before using voice features, start the TTS and STT servers:

**Option 1: Start all servers together (recommended)**
```bash
cd /path/to/7DTD-LLM-NPC-Mod
./start_servers.sh
```

**Option 2: Start servers individually (for debugging)**
```bash
# Terminal 1: TTS Server
./start_tts_server.sh

# Terminal 2: STT Server
./start_stt_server.sh
```

**Stop servers:**
```bash
./stop_servers.sh
```

### Talking to NPCs

**Voice Input (NEW!):**
1. Stand within 15 meters of an NPC
2. **Hold V key** (configurable push-to-talk)
3. **Speak your message**
4. **Release V key**
5. NPC responds with voice and text

**In-Game Chat (text):**
1. Stand near an NPC (within 5 meters)
2. Open chat (T key)
3. Prefix your message with `@`

```
@Hello there, survivor!
@Come with me, I need backup
@Can you guard this area while I loot?
@Do you have any bandages?
@Stop following me, stay here
```

### Console Commands

Press F1 to open console, then:

**General Commands:**
```
llmchat test            # Test LLM server connection
llmchat status          # Show status and performance stats
llmchat talk <message>  # Talk to nearest NPC via console
llmchat list            # List active NPC conversations
llmchat clear           # Clear all conversation history
```

**TTS Commands:**
```
llmchat tts             # Show TTS status
llmchat tts test        # Test TTS with sample text
llmchat tts on/off      # Enable/disable voice output
llmchat tts voices      # List available voices
```

**STT Commands:**
```
llmchat stt             # Show STT status
llmchat stt test        # Test microphone and transcription (3s)
llmchat stt on/off      # Enable/disable voice input
llmchat stt devices     # List available microphones
```

**Action Commands:**
```
llmchat action follow   # Force NPC to follow
llmchat action stop     # Stop NPC from following
llmchat action guard    # Guard current location
llmchat action wait     # Wait at current location
```

### NPC Actions

NPCs can perform these actions based on conversation:

| Action | Example Request | What Happens |
|--------|-----------------|--------------|
| Follow | "Come with me" | NPC follows player |
| Stop | "Stay here" | NPC stops following |
| Wait | "Wait for me" | NPC holds position |
| Guard | "Guard this area" | NPC guards location, attacks hostiles |
| Trade | "Let's trade" | Opens trade window (if trader) |
| Heal | "Can you heal me?" | NPC heals player (if capable) |
| Give | (NPC decides) | NPC gives player items |
| Refuse | "Jump off that cliff" | NPC declines unreasonable requests |

## Configuration

All configuration files are in `Config/`:
- `llmconfig.xml` - LLM server and action settings
- `ttsconfig.xml` - Text-to-speech voice settings
- `sttconfig.xml` - Speech-to-text input settings

### LLM Settings (llmconfig.xml)

The default `Config/llmconfig.xml` is optimized for your RTX 6000 Pro:

```xml
<Server>
    <Model>llama3:70b</Model>       <!-- Full 70B model fits in 48GB! -->
    <TimeoutSeconds>15</TimeoutSeconds>
    <MaxTokens>200</MaxTokens>
    <Temperature>0.8</Temperature>
    <NumGPULayers>83</NumGPULayers> <!-- Full GPU offload -->
    <NumCtx>4096</NumCtx>           <!-- Large context window -->
</Server>
```

### Model Recommendations by Hardware

| GPU VRAM | Recommended Model | Expected Speed |
|----------|-------------------|----------------|
| 48GB+ | llama3:70b | 1-2 seconds |
| 24GB | llama3:70b-q4 or mixtral | 2-3 seconds |
| 16GB | llama3:8b | < 1 second |
| 12GB | mistral:7b | < 1 second |
| 8GB | gemma2:2b | < 0.5 seconds |

### TTS Settings (ttsconfig.xml)

Configure voice output in `Config/ttsconfig.xml`:

```xml
<TTSConfig>
    <Server>
        <Enabled>true</Enabled>
        <Endpoint>http://localhost:5050/synthesize</Endpoint>
        <TimeoutSeconds>10</TimeoutSeconds>
    </Server>
    <Audio>
        <Volume>0.8</Volume>
        <MaxDistance>20</MaxDistance>  <!-- How far you can hear NPCs -->
        <MinDistance>2</MinDistance>   <!-- Distance for full volume -->
        <SpeechRate>1.0</SpeechRate>   <!-- 1.0 = normal speed -->
    </Audio>
    <Voices>
        <DefaultVoice>en_US-lessac-medium</DefaultVoice>
        <TraderVoice>en_US-ryan-medium</TraderVoice>
        <CompanionVoice>en_US-amy-medium</CompanionVoice>
        <BanditVoice>en_US-ryan-medium</BanditVoice>
    </Voices>
</TTSConfig>
```

Available Piper voices (download from [rhasspy/piper-voices](https://huggingface.co/rhasspy/piper-voices)):
- `en_US-lessac-medium` - Neutral, clear
- `en_US-ryan-medium` - Male, deeper
- `en_US-amy-medium` - Female, warm
- `en_US-joe-medium` - Male, casual
- `en_GB-alan-medium` - British male
- Many more available for different languages and accents

### STT Settings (sttconfig.xml)

Configure voice input in `Config/sttconfig.xml`:

```xml
<STTConfig>
    <Server>
        <Enabled>true</Enabled>
        <Endpoint>http://localhost:5051/transcribe</Endpoint>
        <TimeoutSeconds>10</TimeoutSeconds>
    </Server>
    <Audio>
        <SampleRate>16000</SampleRate>           <!-- Whisper requires 16kHz -->
        <MaxRecordingSeconds>15</MaxRecordingSeconds>
    </Audio>
    <Input>
        <PushToTalkKey>V</PushToTalkKey>        <!-- Configurable PTT key -->
    </Input>
    <Whisper>
        <Model>base.en</Model>                   <!-- Balance of speed/accuracy -->
        <Language>en</Language>
    </Whisper>
</STTConfig>
```

**Whisper Model Recommendations:**

| Model | Size | Speed | Accuracy | VRAM |
|-------|------|-------|----------|------|
| tiny.en | 39M | Very fast | Good | < 1GB |
| base.en | 74M | Fast | Better | ~1GB |
| small.en | 244M | Medium | Very good | ~2GB |
| medium.en | 769M | Slow | Excellent | ~5GB |

Use `base.en` for best balance. Change with `llmchat stt` commands or edit config.

### Personality Customization

Edit `Config/personalities.xml` to add custom NPC personalities:

```xml
<Personality id="trader_gruff">
    <Name>Gruff Trader</Name>
    <Traits>You're a no-nonsense trader who's seen too much...</Traits>
</Personality>
```

## Performance

With RTX 6000 Pro and llama3:70b, expect:
- **LLM Response Time**: 1-2 seconds
- **TTS Generation**: < 500ms (Piper is very fast)
- **STT Transcription**: 1-2 seconds (base.en model)
- **Total Voice-to-Voice**: 3-5 seconds
- **Quality**: Excellent roleplay, nuanced responses
- **Actions**: Reliable action parsing

Run `llmchat status` in-game to see performance stats:
```
=== NPC LLM Chat Status ===
LLM Server: Connected (http://localhost:11434)
TTS Server: Connected (http://localhost:5050)
STT Server: Connected (http://localhost:5051)
Active Conversations: 2
Average Response Time: 1250ms
```

## Troubleshooting

### "Connection failed" error
```bash
# Check Ollama is running
curl http://localhost:11434/api/generate -d '{"model":"llama3:70b","prompt":"test"}'

# Restart Ollama
systemctl restart ollama  # or: ollama serve
```

### Slow responses
1. Check GPU utilization: `nvidia-smi`
2. Ensure full GPU offload: Set `NumGPULayers` to 83+ for 70B models
3. Try smaller model: `llama3:8b` for < 1 second responses

### NPCs not responding
- Ensure you're within 3 meters
- Use `@` prefix in chat
- Check `llmchat status` for errors

### Actions not executing
- Actions require clear intent in your message
- Say "come with me" not "maybe you could follow"
- Check console (F1) for action parsing logs

### Voice input not working
```bash
# Check STT server
curl http://localhost:5051/health

# Test microphone
llmchat stt devices  # List available microphones
llmchat stt test     # Record 3 seconds and transcribe

# Check for conflicts with in-game voice chat
# Disable game's built-in voice chat if needed
```

### No voice output from NPCs
```bash
# Check TTS server
curl http://localhost:5050/health

# Test TTS
llmchat tts test

# Verify voice files exist
ls ~/.local/share/piper/voices/*.onnx

# Enable/disable TTS
llmchat tts on
```

### Recording stops immediately or no audio captured
- Hold V key for at least 0.5 seconds before speaking
- Check that no other application is using the microphone
- Disable in-game voice chat (can conflict with microphone access)
- Verify microphone works: `llmchat stt devices`

### "No NPC around to talk to" error
- Make sure you're within 15 meters of an NPC
- NPCs must be from NPCCore/SCore (not vanilla NPCs)
- Check console (F1) for NPC detection logs

## Architecture

**Voice-to-Voice Flow:**
```
Player (Hold V) → MicrophoneCapture → WAV Audio → STTService → Whisper Server
                                                                      ↓
                                                               Transcribed Text
                                                                      ↓
                  NPCChatComponent ← Find Nearest NPC ←──────────────┘
                         ↓
                    LLMService → Ollama → Response Text
                         ↓
                  ┌──────┴──────┐
                  ↓             ↓
           ActionParser    TTSService → Piper Server → Audio
                  ↓                                        ↓
           ActionExecutor                          3D Spatial Audio
                  ↓
           NPC AI Tasks (Follow, Guard, Trade, etc.)
```

**Text Chat Flow:**
```
Player Chat (@message) → Harmony Patch → NPCChatComponent → LLMService → Ollama
                                               ↓
                                         ActionParser
                                               ↓
                                         ActionExecutor → NPC AI Tasks
```

## License

MIT - Feel free to modify and share!

## Credits

- NPCCore/SCore team for the NPC framework
- Ollama team for local LLM inference
- Rhasspy/Piper team for neural TTS
- OpenAI Whisper team for speech recognition
- faster-whisper contributors for optimized inference
- 7DTD Modding community
