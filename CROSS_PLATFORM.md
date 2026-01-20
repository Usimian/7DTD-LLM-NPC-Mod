# Cross-Platform Architecture

This document explains how NPCLLMChat supports both **Windows 11** and **Ubuntu 24** with a single codebase.

## Overview

**One DLL works on both platforms.** The mod detects the operating system at runtime and automatically uses the appropriate TTS/STT backend.

```
┌─────────────────────────────────────────────────────────────┐
│                   Same NPCLLMChat.dll                       │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   On Startup: PlatformHelper.IsWindows?                     │
│                      │                                      │
│          ┌──────────┴──────────┐                           │
│          ▼                     ▼                           │
│   ┌─────────────┐       ┌─────────────┐                    │
│   │  WINDOWS    │       │   LINUX     │                    │
│   ├─────────────┤       ├─────────────┤                    │
│   │ TTS: SAPI   │       │ TTS: Piper  │                    │
│   │ (built-in)  │       │ (HTTP:5050) │                    │
│   ├─────────────┤       ├─────────────┤                    │
│   │ STT: Win SR │       │ STT: Whisper│                    │
│   │ (built-in)  │       │ (HTTP:5051) │                    │
│   └─────────────┘       └─────────────┘                    │
│                                                             │
│   LLM: Ollama (same on both - HTTP:11434)                  │
└─────────────────────────────────────────────────────────────┘
```

## Platform Detection

Runtime detection is handled by `PlatformHelper.cs`:

```csharp
public static bool IsWindows => 
    Application.platform == RuntimePlatform.WindowsPlayer ||
    Environment.OSVersion.Platform == PlatformID.Win32NT;

public static bool IsLinux =>
    Application.platform == RuntimePlatform.LinuxPlayer ||
    Environment.OSVersion.Platform == PlatformID.Unix;
```

## Service Provider Selection

Both `TTSService.cs` and `STTService.cs` use the same pattern:

```csharp
switch (_config.Provider)
{
    case TTSProvider.Auto:
        if (PlatformHelper.IsWindows)
        {
            _activeProvider = TTSProvider.Windows;  // Use Windows SAPI
            InitializeWindows();
        }
        else
        {
            _activeProvider = TTSProvider.Piper;    // Use Piper HTTP server
            StartCoroutine(InitializePiper());
        }
        break;
        
    case TTSProvider.Windows:
        _activeProvider = TTSProvider.Windows;
        InitializeWindows();
        break;
        
    case TTSProvider.Piper:
        _activeProvider = TTSProvider.Piper;
        StartCoroutine(InitializePiper());
        break;
}
```

## Conditional Compilation

Windows-specific code uses `#if UNITY_STANDALONE_WIN` to avoid compilation errors on Linux:

```csharp
// In WindowsTTSProvider.cs
#if UNITY_STANDALONE_WIN
using System.Speech.Synthesis;  // Only available on Windows
#endif

public void Synthesize(string text, string voice, float rate, Action<byte[]> onSuccess, Action<string> onError)
{
#if UNITY_STANDALONE_WIN
    // Windows SAPI implementation
    using (var synth = new SpeechSynthesizer())
    {
        synth.SetOutputToWaveStream(stream);
        synth.Speak(text);
        // ...
    }
#else
    onError?.Invoke("Windows TTS not available on this platform");
#endif
}
```

## Key Files

| File | Purpose |
|------|---------|
| `Scripts/PlatformHelper.cs` | Runtime platform detection |
| `Scripts/TTS/WindowsTTSProvider.cs` | Windows SAPI implementation |
| `Scripts/STT/WindowsSTTProvider.cs` | Windows Speech Recognition implementation |
| `Scripts/TTS/TTSService.cs` | Routes to Windows SAPI or Piper based on platform |
| `Scripts/STT/STTService.cs` | Routes to Windows Speech or Whisper based on platform |

## Provider Implementations

### TTS (Text-to-Speech)

| Provider | Platform | Implementation | External Dependency |
|----------|----------|----------------|---------------------|
| Windows SAPI | Windows | `System.Speech.Synthesis` | None (built into Windows) |
| Piper | Any | HTTP POST to `localhost:5050` | Piper server required |

### STT (Speech-to-Text)

| Provider | Platform | Implementation | External Dependency |
|----------|----------|----------------|---------------------|
| Windows Speech | Windows | `System.Speech.Recognition` | None (built into Windows) |
| Whisper | Any | HTTP POST to `localhost:5051` | Whisper server required |

### LLM (Large Language Model)

| Provider | Platform | Implementation | External Dependency |
|----------|----------|----------------|---------------------|
| Ollama | Any | HTTP POST to `localhost:11434` | Ollama required |

## Configuration Override

Users can force a specific provider in the config files:

**ttsconfig.xml:**
```xml
<Server>
    <!-- Options: Auto, Windows, Piper -->
    <Provider>Auto</Provider>
</Server>
```

**sttconfig.xml:**
```xml
<Server>
    <!-- Options: Auto, Windows, Whisper -->
    <Provider>Auto</Provider>
</Server>
```

## Build Process

A single build produces a DLL that works on both platforms:

```bash
cd NPCLLMChat
dotnet build -c Release
```

The resulting `NPCLLMChat.dll` contains all code paths. The runtime platform detection determines which path executes.

## Platform-Specific Setup

### Windows 11

1. Install Ollama
2. Install mod
3. Play! (TTS/STT work automatically using Windows built-in features)

### Ubuntu 24

1. Install Ollama
2. Set up Piper TTS server
3. Set up Whisper STT server
4. Run `./start_servers.sh`
5. Install mod
6. Play!

## Summary

| Aspect | Implementation |
|--------|---------------|
| **Codebase** | Single - same source files for both platforms |
| **Binary** | Single - same DLL works on Windows and Linux |
| **Detection** | Runtime via `PlatformHelper.IsWindows` / `IsLinux` |
| **Windows TTS/STT** | Uses `System.Speech` namespace (built into .NET Framework) |
| **Linux TTS/STT** | Uses HTTP calls to external Piper/Whisper servers |
| **Conditional Code** | `#if UNITY_STANDALONE_WIN` for Windows-only APIs |
| **User Override** | `<Provider>` setting in XML config files |
