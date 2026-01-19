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
import subprocess
import sys
from pathlib import Path
from flask import Flask, request, jsonify, send_file

app = Flask(__name__)

# Configuration
VOICES_DIR = Path.home() / ".local/share/piper/voices"
PIPER_BINARY = Path.home() / ".local/bin/piper"
DEFAULT_VOICE = "en_US-lessac-medium"


def get_available_voices():
    """Scan voices directory for available voice models"""
    voices = []
    if VOICES_DIR.exists():
        for onnx_file in VOICES_DIR.glob("*.onnx"):
            voice_name = onnx_file.stem
            json_file = onnx_file.with_suffix(".onnx.json")
            if json_file.exists():
                voices.append({
                    "id": voice_name,
                    "model": str(onnx_file),
                    "config": str(json_file)
                })
    return voices


@app.route("/health", methods=["GET"])
def health():
    """Health check endpoint"""
    return jsonify({
        "status": "ok",
        "piper_available": PIPER_BINARY.exists(),
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
            "length_scale": 1.0,              // optional, speaking speed
            "noise_scale": 0.667,             // optional, voice variation
            "noise_w_scale": 0.8              // optional, phoneme width variation
        }

    Returns: WAV audio file
    """
    try:
        data = request.get_json()

        if not data or "text" not in data:
            return jsonify({"error": "Missing 'text' field"}), 400

        text = data["text"].strip()
        if not text:
            return jsonify({"error": "Empty text"}), 400

        # Get voice model
        voice = data.get("voice", DEFAULT_VOICE)

        # Print the full text being synthesized
        print(f"Synthesizing ({voice}): \"{text}\"", flush=True)
        model_path = VOICES_DIR / f"{voice}.onnx"

        if not model_path.exists():
            return jsonify({
                "error": f"Voice '{voice}' not found",
                "available": [v["id"] for v in get_available_voices()]
            }), 404

        # Use temp files for input and output
        import tempfile

        # Create temp files
        with tempfile.NamedTemporaryFile(mode='w', suffix=".txt", delete=False) as f:
            input_path = f.name
            f.write(text)

        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            output_path = f.name

        try:
            # Build piper command
            cmd = [
                str(PIPER_BINARY),
                "--model", str(model_path),
                "--input-file", input_path,
                "--output-file", output_path
            ]

            # Optional parameters
            if "speaker" in data:
                cmd.extend(["--speaker", str(data["speaker"])])
            if "length_scale" in data:
                cmd.extend(["--length-scale", str(data["length_scale"])])
            if "noise_scale" in data:
                cmd.extend(["--noise-scale", str(data["noise_scale"])])
            if "noise_w_scale" in data:
                cmd.extend(["--noise-w-scale", str(data["noise_w_scale"])])

            result = subprocess.run(
                cmd,
                capture_output=True,
                timeout=30
            )

            if result.returncode != 0:
                return jsonify({
                    "error": "Piper synthesis failed",
                    "details": result.stderr.decode("utf-8", errors="ignore")
                }), 500

            # Read the WAV file and return it
            with open(output_path, "rb") as f:
                wav_data = f.read()

            return send_file(
                io.BytesIO(wav_data),
                mimetype="audio/wav",
                as_attachment=False,
                download_name="speech.wav"
            )
        finally:
            # Clean up temp files
            if os.path.exists(input_path):
                os.unlink(input_path)
            if os.path.exists(output_path):
                os.unlink(output_path)

    except subprocess.TimeoutExpired:
        return jsonify({"error": "Synthesis timed out"}), 504
    except Exception as e:
        return jsonify({"error": str(e)}), 500


def main():
    global VOICES_DIR, PIPER_BINARY, DEFAULT_VOICE

    parser = argparse.ArgumentParser(description="Piper TTS HTTP Server")
    parser.add_argument("--port", type=int, default=5050, help="Port to listen on")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    parser.add_argument("--voices-dir", type=str, help="Directory containing voice models")
    parser.add_argument("--piper-binary", type=str, help="Path to piper binary")
    parser.add_argument("--default-voice", type=str, default=DEFAULT_VOICE,
                        help="Default voice to use")
    args = parser.parse_args()

    if args.voices_dir:
        VOICES_DIR = Path(args.voices_dir)
    if args.piper_binary:
        PIPER_BINARY = Path(args.piper_binary)
    DEFAULT_VOICE = args.default_voice

    # Validate setup
    if not PIPER_BINARY.exists():
        print(f"Error: Piper binary not found at {PIPER_BINARY}")
        print("Install with: pipx install piper-tts")
        sys.exit(1)

    voices = get_available_voices()
    if not voices:
        print(f"Warning: No voice models found in {VOICES_DIR}")
        print("Download voices from https://huggingface.co/rhasspy/piper-voices")
    else:
        print(f"Found {len(voices)} voice(s): {', '.join(v['id'] for v in voices)}")

    print(f"Starting Piper TTS server on http://{args.host}:{args.port}")
    print(f"  Voices directory: {VOICES_DIR}")
    print(f"  Default voice: {DEFAULT_VOICE}")
    print(f"Endpoints:")
    print(f"  GET  /health     - Health check")
    print(f"  GET  /voices     - List voices")
    print(f"  POST /synthesize - Text to speech")

    app.run(host=args.host, port=args.port, debug=False)


if __name__ == "__main__":
    main()
