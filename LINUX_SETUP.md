# Linux Setup (Ubuntu) — Full Rebuild Guide

Complete recipe to get NPCLLMChat running on a fresh Ubuntu machine with the native
Linux Steam build of 7 Days to Die. Verified working 2026-07-24 on Ubuntu 24.04,
7DTD V2.6 stable.

## Architecture on Linux

All AI traffic is HTTP on localhost:

| Service | Port  | Runs as |
|---------|-------|---------|
| Ollama (LLM)        | 11434 | system service (ollama installer) |
| Piper (TTS)         | 5050  | systemd **user** service `piper-tts` |
| faster-whisper (STT)| 5051  | systemd **user** service `whisper-stt` |

**Why external services, not in-game auto-start:** Steam runs the game inside its
Linux Runtime container (pressure-vessel). The container shares the host network —
so the game reaches all three ports — but NOT the host filesystem's `/usr`, so the
game cannot exec host Python. `ServerManager` therefore detects an already-listening
server on 5050/5051 and uses it, only spawning its own when a port is free (relevant
on Windows or non-Steam installs).

## 1. Prerequisites

```bash
sudo apt install dotnet-sdk-8.0 python3-venv
# Ollama: https://ollama.com/install.sh
ollama pull qwen3.6:35b        # or whatever model llmconfig.xml names
```

Steam → install 7 Days to Die (native Linux build). Then install into
`~/.steam/steam/steamapps/common/7 Days To Die/Mods/`:
- 0-SCore (match game version)
- 0_TFP_Harmony (ships with recent SCore packages if not present)
- 0-XNPCCore
- (optional) The Wasteland — coexists fine

**EAC must be disabled** in the 7DTD launcher or DLL mods will not load.

## 2. Clone and build

```bash
git clone git@github.com:Usimian/7DTD-LLM-NPC-Mod.git ~/7DTD-LLM-NPC-Mod
cd ~/7DTD-LLM-NPC-Mod
dotnet build NPCLLMChat/NPCLLMChat.csproj -c Release
```

The csproj auto-selects Linux game paths (`~/.steam/steam/...`).

## 3. Python venvs for the speech servers

```bash
./setup_servers.sh
```

Creates `piper-server/venv` and `whisper-server/venv` and installs deps.
The systemd units below reference these venvs by absolute path, so the repo must
live at `~/7DTD-LLM-NPC-Mod` (or edit the unit files).

## 4. Piper voice models

The Piper server scans `~/.local/share/piper/voices/` (no auto-download):

```bash
mkdir -p ~/.local/share/piper/voices && cd ~/.local/share/piper/voices
for v in "lessac en_US-lessac-medium" "ryan en_US-ryan-medium" "amy en_US-amy-medium"; do
  set -- $v
  for ext in onnx onnx.json; do
    curl -sL -O "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/$1/medium/$2.$ext"
  done
done
```

These three voices are what `Config/ttsconfig.xml` maps to default/trader/companion/bandit.

## 5. Speech servers as systemd user services

```bash
mkdir -p ~/.config/systemd/user
cp piper-server/piper-tts.service whisper-server/whisper-stt.service ~/.config/systemd/user/
systemctl --user daemon-reload
systemctl --user enable --now piper-tts whisper-stt
# verify
curl http://localhost:5050/health   # {"piper_available":true,...,"voices_count":3}
curl http://localhost:5051/health   # {"model_loaded":true,...}
```

Idle cost ≈ 340 MB RAM total, ~0% CPU. Logs: `journalctl --user -u piper-tts -f`.

## 6. Deploy the mod (symlinks — rebuild = redeploy)

```bash
GAME_MODS="$HOME/.steam/steam/steamapps/common/7 Days To Die/Mods"
mkdir -p "$GAME_MODS/NPCLLMChat" && cd "$GAME_MODS/NPCLLMChat"
ln -sfn ~/7DTD-LLM-NPC-Mod/NPCLLMChat/bin/Release/NPCLLMChat.dll NPCLLMChat.dll
ln -sfn ~/7DTD-LLM-NPC-Mod/NPCLLMChat/ModInfo.xml ModInfo.xml
ln -sfn ~/7DTD-LLM-NPC-Mod/NPCLLMChat/Config Config
ln -sfn ~/7DTD-LLM-NPC-Mod/piper-server piper-server
ln -sfn ~/7DTD-LLM-NPC-Mod/whisper-server whisper-server
```

## 7. Model configuration gotcha

`Config/llmconfig.xml` sets the model, **but** a saved in-game setting overrides it:
Unity PlayerPrefs key `NPCLLMChat_Model`. If NPCs use the wrong model, open the mod's
settings window in-game and set the Model field (e.g. `qwen3.6:35b`) — applies
immediately and persists.

The C# request sends `"think": false` — required for thinking models (qwen3 family),
which otherwise burn the whole token budget on reasoning and return empty responses.
Non-thinking models ignore the flag.

## 8. In-game testing

F1 console:

```
llmchat test          # LLM round trip
llmchat tts test      # run twice if servers started after the game did
llmchat stt refresh   # re-detect Whisper (availability is cached at startup)
llmchat stt test      # 3s mic recording → transcription
llmchat status
```

Spawning a friendly NPC to talk to (`dm` first, stand on open ground outdoors):

```
lpi                        # your player entity id
se <playerid> npcNursePistol
```

Friendly (whiteriver faction): `npcNurse*`, `npcBaker*`, Wasteland `humanSurvivor*`.
**`npcHarley*` is faction bandits and will attack.**
Alternative: `cm`, then U → search "npc" → placeable spawner items.

Then talk: type `@Hello there` in chat near the NPC, or hold **V** and speak
(Unity microphone capture works on native Linux).

## Troubleshooting

- **"server not available" in-game but curl works** — the mod caches availability at
  startup. `llmchat stt refresh` / `llmchat tts test` re-check, or restart the game
  (the mod detects already-running servers at startup).
- **Empty NPC responses, ~15s delays** — thinking model without `"think": false`
  (fixed in `LLMService.cs`), or the PlayerPrefs model override points at a model
  not pulled in Ollama.
- **Mod doesn't load** — EAC still enabled, or symlink targets missing (rebuild step 2).
- **Game log** — `~/.local/share/7DaysToDie/logs/output_log_client__*.txt`,
  grep `NPCLLMChat`.
