# TTS Integration Plan for NPCLLMChat

## Overview

This document outlines the plan to add Text-to-Speech (TTS) capabilities to the NPC LLM Chat mod, enabling NPCs to speak their responses audibly with 3D spatial audio.

## TTS Engine Comparison

### Option 1: Piper TTS (Recommended for Initial Implementation)

**Pros:**
- Extremely fast (optimized for Raspberry Pi, will fly on RTX 6000)
- Simple HTTP API available via `piper --http`
- Lightweight ONNX models (50-100MB per voice)
- Many voice options (100+ voices across languages)
- Actively maintained (v1.3.0 as of July 2025)
- No GPU required (but can use CPU efficiently)

**Cons:**
- No voice cloning (fixed voice models)
- Less natural than XTTS for long-form speech

**Installation:**
```bash
pip install piper-tts
# Download voices from https://huggingface.co/rhasspy/piper-voices
piper --http --model en_US-lessac-medium.onnx
```

**API Endpoint:** `POST http://localhost:5000/synthesize`

### Option 2: Coqui XTTS v2

**Pros:**
- Voice cloning from 6-second audio sample
- Very natural sounding
- Supports streaming (<200ms latency)
- 17 languages supported

**Cons:**
- Requires GPU for reasonable speed
- Higher VRAM usage (~4-6GB)
- More complex setup
- Single-stream limitation (one voice at a time)

**Installation:**
```bash
pip install xtts-api-server
xtts-api-server --deepspeed  # Uses GPU acceleration
```

**API Endpoint:** `POST http://localhost:8020/tts_to_audio`

### Recommendation

**Phase 1:** Start with Piper TTS for reliability and speed
**Phase 2:** Add XTTS support for voice cloning (different NPC voices from samples)

---

## Architecture Design

### New Files to Create

```
NPCLLMChat/
├── Scripts/
│   ├── TTS/
│   │   ├── TTSService.cs          # Main TTS HTTP client (like LLMService)
│   │   ├── TTSConfig.cs           # TTS configuration model
│   │   ├── NPCAudioPlayer.cs      # Per-NPC audio playback component
│   │   └── VoiceProfile.cs        # Voice settings per NPC type
│   └── ...
├── Config/
│   ├── llmconfig.xml
│   └── ttsconfig.xml              # TTS-specific configuration
└── Resources/
    └── VoiceSamples/              # Reference audio for XTTS voice cloning
```

### TTSService.cs - Core Service

```csharp
public class TTSService : MonoBehaviour
{
    private static TTSService _instance;
    public static TTSService Instance { get; }

    // Configuration
    private string _endpoint;
    private string _voiceModel;
    private TTSProvider _provider; // Piper, XTTS, etc.

    // Request queue to prevent overlapping audio
    private Queue<TTSRequest> _requestQueue;
    private bool _isProcessing;

    // Main API
    public void SynthesizeSpeech(
        string text,
        string voiceId,
        Action<AudioClip> onSuccess,
        Action<string> onError
    );

    // Streaming API (for XTTS)
    public void SynthesizeSpeechStreaming(
        string text,
        string voiceId,
        Action<AudioClip> onChunkReady,  // Called multiple times
        Action onComplete,
        Action<string> onError
    );
}
```

### NPCAudioPlayer.cs - Per-NPC Audio Component

```csharp
public class NPCAudioPlayer : MonoBehaviour
{
    private AudioSource _audioSource;
    private EntityAlive _npcEntity;

    // 3D spatial audio settings
    private float _maxDistance = 20f;
    private float _minDistance = 2f;

    public void Initialize(EntityAlive npc)
    {
        _npcEntity = npc;
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 1.0f;  // Full 3D
        _audioSource.rolloffMode = AudioRolloffMode.Linear;
        _audioSource.maxDistance = _maxDistance;
        _audioSource.minDistance = _minDistance;
    }

    public void PlaySpeech(AudioClip clip);
    public void StopSpeaking();
    public bool IsSpeaking { get; }
}
```

### Integration with Existing NPCChatComponent

```csharp
// In NPCChatComponent.HandleLLMResponse():
private void HandleLLMResponse(string response, Action<string> onComplete)
{
    // ... existing parsing logic ...

    // NEW: Trigger TTS if enabled
    if (_config.TTSEnabled && !string.IsNullOrEmpty(dialogueResponse))
    {
        _audioPlayer.PlaySpeech(dialogueResponse, _voiceProfile);
    }

    // ... existing display logic ...
}
```

---

## Configuration (ttsconfig.xml)

```xml
<?xml version="1.0" encoding="UTF-8"?>
<TTSConfig>
    <Server>
        <!-- TTS Provider: Piper, XTTS, or Disabled -->
        <Provider>Piper</Provider>

        <!-- Piper endpoint -->
        <PiperEndpoint>http://localhost:5000/synthesize</PiperEndpoint>

        <!-- XTTS endpoint (if using) -->
        <XTTSEndpoint>http://localhost:8020/tts_to_audio</XTTSEndpoint>

        <!-- Request timeout -->
        <TimeoutSeconds>10</TimeoutSeconds>
    </Server>

    <Audio>
        <!-- Master volume (0.0 - 1.0) -->
        <Volume>0.8</Volume>

        <!-- 3D audio settings -->
        <MaxDistance>20</MaxDistance>
        <MinDistance>2</MinDistance>

        <!-- Speaking speed multiplier -->
        <SpeechRate>1.0</SpeechRate>
    </Audio>

    <Voices>
        <!-- Default voice for NPCs -->
        <DefaultVoice>en_US-lessac-medium</DefaultVoice>

        <!-- Voice mappings by NPC type (for variety) -->
        <VoiceMapping npcType="trader" voice="en_US-ryan-medium" />
        <VoiceMapping npcType="companion" voice="en_US-amy-medium" />
        <VoiceMapping npcType="bandit" voice="en_US-joe-medium" />
    </Voices>
</TTSConfig>
```

---

## Data Flow

```
Player Message
      │
      ▼
┌─────────────────┐
│  LLMService     │  (existing)
│  Ollama/LLM     │
└────────┬────────┘
         │ Text Response
         ▼
┌─────────────────┐
│ NPCChatComponent│  (existing, modified)
│ Parse & Display │
└────────┬────────┘
         │ Text to speak
         ▼
┌─────────────────┐
│   TTSService    │  (new)
│ HTTP to Piper   │
└────────┬────────┘
         │ WAV audio bytes
         ▼
┌─────────────────┐
│ NPCAudioPlayer  │  (new)
│ 3D AudioSource  │
└────────┬────────┘
         │
         ▼
    🔊 NPC Speaks
```

---

## Implementation Phases

### Phase 1: Basic Piper Integration (MVP)
1. Create TTSService.cs with Piper HTTP client
2. Create NPCAudioPlayer.cs for audio playback
3. Add ttsconfig.xml configuration
4. Integrate into NPCChatComponent
5. Add console commands: `llmchat tts on/off`, `llmchat voice <name>`

### Phase 2: Voice Variety
1. Add VoiceProfile system for different NPC types
2. Map NPC entity types to voice models
3. Allow per-NPC voice customization

### Phase 3: XTTS Voice Cloning (Optional)
1. Add XTTS provider support
2. Voice sample management system
3. Runtime voice cloning from audio files

### Phase 4: Polish
1. Lip sync animation hooks (if 7DTD supports)
2. Subtitle display synchronization
3. Audio ducking (lower game audio while NPC speaks)
4. Interrupt handling (stop speaking if player walks away)

---

## Console Commands (Extended)

```
llmchat tts             - Show TTS status
llmchat tts on          - Enable TTS
llmchat tts off         - Disable TTS
llmchat tts test        - Speak a test phrase
llmchat tts voice       - List available voices
llmchat tts voice <id>  - Set default voice
llmchat tts volume <n>  - Set volume (0-100)
```

---

## Dependencies

### Piper TTS Setup (One-time)
```bash
# Install Piper
pip install piper-tts

# Download a voice model (example: lessac medium quality)
mkdir -p ~/.local/share/piper/voices
cd ~/.local/share/piper/voices
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx
wget https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json

# Start Piper HTTP server
piper --http --model ~/.local/share/piper/voices/en_US-lessac-medium.onnx
```

### Unity Audio Requirements
- UnityEngine.AudioModule (already referenced)
- WAV parsing (built into DownloadHandlerAudioClip)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| TTS server not running | Graceful fallback to text-only |
| Slow TTS generation | Queue requests, show speaking indicator |
| Audio format issues | Normalize to 16-bit PCM WAV |
| Multiple NPCs speaking | Audio priority queue, distance-based |
| Large audio memory usage | Stream audio, limit cache size |

---

## Success Metrics

- [ ] TTS generates audio in < 500ms for typical NPC response
- [ ] 3D audio correctly positioned at NPC location
- [ ] No audio overlap issues
- [ ] Graceful fallback when TTS unavailable
- [ ] Volume and distance settings work correctly
