#!/usr/bin/env python3
"""
Simple HTTP server for Piper TTS
Provides a REST API for text-to-speech synthesis

Usage:
    python piper_server.py [--port 5050] [--voices-dir ~/.local/share/piper/voices]

Endpoints:
    GET  /health              - Health check
    GET  /voices              - List available voices
    POST /synthesize          - Convert text to speech (returns WAV)

Example:
    curl -X POST http://localhost:5050/synthesize \
         -H "Content-Type: application/json" \
         -d '{"text": "Hello world", "voice": "en_US-lessac-medium"}' \
         --output speech.wav
"""

import argparse
import io
import os
import platform
import sys
import wave
from pathlib import Path
from flask import Flask, request, jsonify, send_file

try:
    from piper import PiperVoice
    PIPER_AVAILABLE = True
except ImportError:
    PIPER_AVAILABLE = False
    print("Warning: piper-tts not installed. Install with: pip install piper-tts")

app = Flask(__name__)

# Platform-specific default paths
if platform.system() == "Windows":
    VOICES_DIR = Path.home() / "AppData/Local/piper/voices"
else:
    VOICES_DIR = Path.home() / ".local/share/piper/voices"

DEFAULT_VOICE = "en_US-lessac-medium"

# Cache loaded voices
_voice_cache = {}


def get_available_voices():
    """Scan voices directory for available voice models"""
    voices = []
    if VOICES_DIR.exists():
        for onnx_file in VOICES_DIR.glob("*.onnx"):
            # Skip .onnx.json files
            if onnx_file.suffix == ".json":
                continue
            voice_name = onnx_file.stem
            json_file = VOICES_DIR / f"{voice_name}.onnx.json"
            if json_file.exists():
                voices.append({
                    "id": voice_name,
                    "model": str(onnx_file),
                    "config": str(json_file)
                })
    return voices


def load_voice(voice_id):
    """Load a voice model (with caching)"""
    if voice_id in _voice_cache:
        return _voice_cache[voice_id]

    model_path = VOICES_DIR / f"{voice_id}.onnx"
    config_path = VOICES_DIR / f"{voice_id}.onnx.json"

    if not model_path.exists() or not config_path.exists():
        return None

    try:
        voice = PiperVoice.load(str(model_path), config_path=str(config_path))
        _voice_cache[voice_id] = voice
        return voice
    except Exception as e:
        print(f"Error loading voice {voice_id}: {e}")
        return None


@app.route("/health", methods=["GET"])
def health():
    """Health check endpoint"""
    return jsonify({
        "status": "ok",
        "piper_available": PIPER_AVAILABLE,
        "voices_count": len(get_available_voices())
    })


@app.route("/voices", methods=["GET"])
def list_voices():
    """List available voice models"""
    return jsonify({
        "voices": get_available_voices(),
        "default": DEFAULT_VOICE
    })


@app.route("/synthesize", methods=["POST"])
def synthesize():
    """
    Synthesize text to speech

    Request JSON:
        {
            "text": "Text to speak",
            "voice": "en_US-lessac-medium",  // optional, defaults to DEFAULT_VOICE
            "speaker": 0,                     // optional, for multi-speaker models
            "length_scale": 1.0,              // optional, speaking speed (default 1.0)
        }

    Returns: WAV audio file
    """
    if not PIPER_AVAILABLE:
        return jsonify({"error": "piper-tts not installed"}), 500

    try:
        data = request.get_json()

        if not data or "text" not in data:
            return jsonify({"error": "Missing 'text' field"}), 400

        text = data["text"].strip()
        if not text:
            return jsonify({"error": "Empty text"}), 400

        # Get voice model
        voice_id = data.get("voice", DEFAULT_VOICE)
        print(f"Synthesizing ({voice_id}): \"{text}\"", flush=True)

        # Load voice
        voice = load_voice(voice_id)
        if voice is None:
            return jsonify({
                "error": f"Voice '{voice_id}' not found",
                "available": [v["id"] for v in get_available_voices()]
            }), 404

        # Synthesize to WAV bytes
        wav_buffer = io.BytesIO()
        
        # Wrap BytesIO in a wave.Wave_write object
        with wave.open(wav_buffer, 'wb') as wav_file:
            voice.synthesize_wav(text, wav_file)
        
        wav_buffer.seek(0)
        return send_file(
            wav_buffer,
            mimetype="audio/wav",
            as_attachment=False,
            download_name="speech.wav"
        )

    except Exception as e:
        import traceback
        print(f"Error during synthesis: {e}")
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500


def main():
    global VOICES_DIR, DEFAULT_VOICE

    parser = argparse.ArgumentParser(description="Piper TTS HTTP Server")
    parser.add_argument("--port", type=int, default=5050, help="Port to listen on")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    parser.add_argument("--voices-dir", type=str, help="Directory containing voice models")
    parser.add_argument("--default-voice", type=str, default=DEFAULT_VOICE,
                        help="Default voice to use")
    args = parser.parse_args()

    if args.voices_dir:
        VOICES_DIR = Path(args.voices_dir)
    DEFAULT_VOICE = args.default_voice

    # Validate setup
    if not PIPER_AVAILABLE:
        print("Error: piper-tts not installed")
        print("Install with: pip install piper-tts")
        sys.exit(1)

    # Ensure voices directory exists
    VOICES_DIR.mkdir(parents=True, exist_ok=True)

    voices = get_available_voices()
    if not voices:
        print(f"Warning: No voice models found in {VOICES_DIR}")
        print("Download voices from https://huggingface.co/rhasspy/piper-voices")
        print(f"\nExample (Windows):")
        print(f'  curl -L -o "{VOICES_DIR}\\en_US-lessac-medium.onnx" ^')
        print(f'       "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx"')
        print(f'  curl -L -o "{VOICES_DIR}\\en_US-lessac-medium.onnx.json" ^')
        print(f'       "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json"')
    else:
        print(f"Found {len(voices)} voice(s): {', '.join(v['id'] for v in voices)}")

    print(f"\nStarting Piper TTS server on http://{args.host}:{args.port}")
    print(f"  Voices directory: {VOICES_DIR}")
    print(f"  Default voice: {DEFAULT_VOICE}")
    print(f"\nEndpoints:")
    print(f"  GET  /health     - Health check")
    print(f"  GET  /voices     - List voices")
    print(f"  POST /synthesize - Text to speech")
    print()

    app.run(host=args.host, port=args.port, debug=False)


if __name__ == "__main__":
    main()
