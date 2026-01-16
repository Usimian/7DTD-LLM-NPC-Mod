# NPC LLM Chat for 7 Days to Die

Add AI-powered conversations to NPCCore NPCs using a local LLM (Ollama, LM Studio, etc.). NPCs can understand requests and take actions like following you, guarding areas, trading, and more!

## Features

- **Natural Conversations**: Talk to NPCs naturally using chat
- **Context Memory**: NPCs remember previous exchanges
- **Dynamic Actions**: NPCs can follow, guard, trade, heal, and more based on conversation
- **Multiple Personalities**: Each NPC has unique personality traits
- **GPU Optimized**: Configured for high-end GPUs like RTX 6000 Pro
- **Performance Tracking**: Built-in benchmarking and response time monitoring

## Requirements

- 7 Days to Die (A21+)
- [NPCCore](https://www.nexusmods.com/7daystodie/mods/123) / SCore
- Local LLM server (Ollama recommended)
- [Harmony](https://github.com/pardeike/Harmony) (usually included with SCore)
- .NET 4.8 SDK (for building)

## Installation

### 1. Set Up Ollama with GPU Support

```bash
# Install Ollama (Linux)
curl -fsSL https://ollama.com/install.sh | sh

# Pull a model (for RTX 6000 Pro with 48GB VRAM, use the full 70B model!)
ollama pull llama3:70b

# Or for faster responses:
ollama pull llama3:8b

# Start Ollama server
ollama serve
```

### 2. Build the Mod

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

### 3. Install the Mod

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

### Talking to NPCs

**In-Game Chat** (primary method):
1. Stand near an NPC (within 3 meters)
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

```
llmchat test            # Test LLM server connection
llmchat status          # Show status and performance stats
llmchat benchmark       # Run 5-request performance test
llmchat talk <message>  # Talk to nearest NPC via console
llmchat action follow   # Force NPC to follow (for testing)
llmchat action stop     # Stop NPC from following
llmchat list            # List active NPC conversations
llmchat clear           # Clear all conversation history
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

### GPU-Optimized Settings (RTX 6000 Pro)

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
- **Response Time**: 1-2 seconds
- **Quality**: Excellent roleplay, nuanced responses
- **Actions**: Reliable action parsing

Run `llmchat benchmark` in-game to test your setup:
```
=== Benchmark Complete ===
Total Time: 4500ms
Average: 900ms per request
Performance: GOOD
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

## Architecture

```
Player Chat → Harmony Patch → NPCChatComponent → LLMService → Ollama
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
- 7DTD Modding community
