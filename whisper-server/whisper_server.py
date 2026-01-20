#!/usr/bin/env python3
"""
Simple HTTP server for Whisper STT (Speech-to-Text)
Provides a REST API for speech recognition using faster-whisper

Usage:
    python whisper_server.py [--port 5051] [--model base.en]

Endpoints:
    GET  /health              - Health check
    GET  /models              - List available Whisper models
    POST /transcribe          - Convert audio to text (accepts WAV)

Example:
    curl -X POST http://localhost:5051/transcribe \
         -H "Content-Type: audio/wav" \
         --data-binary @recording.wav

Dependencies:
    pip install faster-whisper flask
"""

import argparse
import io
import os
import sys
import tempfile
import time
from pathlib import Path

from flask import Flask, request, jsonify

app = Flask(__name__)

# Configuration
MODEL_NAME = "base.en"
DEVICE = "cpu"  # Force CPU to avoid CUDA library issues on Windows
COMPUTE_TYPE = "int8"  # int8 for CPU efficiency

# Whisper model instance (loaded on first request or startup)
whisper_model = None

# Available models (subset - more exist)
AVAILABLE_MODELS = [
    "tiny", "tiny.en",
    "base", "base.en",
    "small", "small.en",
    "medium", "medium.en",
    "large-v2", "large-v3"
]


def get_model():
    """Get or load the Whisper model"""
    global whisper_model
    if whisper_model is None:
        from faster_whisper import WhisperModel
        print(f"Loading Whisper model '{MODEL_NAME}' on {DEVICE}...")
        start = time.time()
        whisper_model = WhisperModel(
            MODEL_NAME,
            device=DEVICE,
            compute_type=COMPUTE_TYPE
        )
        print(f"Model loaded in {time.time() - start:.1f}s")
    return whisper_model


@app.route("/health", methods=["GET"])
def health():
    """Health check endpoint"""
    model_loaded = whisper_model is not None
    return jsonify({
        "status": "ok",
        "model": MODEL_NAME,
        "model_loaded": model_loaded,
        "device": DEVICE
    })


@app.route("/models", methods=["GET"])
def list_models():
    """List available Whisper models"""
    return jsonify({
        "models": AVAILABLE_MODELS,
        "current": MODEL_NAME,
        "recommended": {
            "fast": "tiny.en",
            "balanced": "base.en",
            "accurate": "small.en"
        }
    })


@app.route("/transcribe", methods=["POST"])
def transcribe():
    """
    Transcribe audio to text

    Accepts:
        - audio/wav content type with WAV data in body
        - multipart/form-data with 'audio' file field

    Returns:
        {
            "text": "Transcribed text",
            "language": "en",
            "duration": 2.5,
            "processing_time": 0.3,
            "segments": [...]  // optional, detailed segments
        }
    """
    try:
        start_time = time.time()

        # Get audio data
        audio_data = None

        if request.content_type and "multipart/form-data" in request.content_type:
            # Handle file upload
            if "audio" not in request.files:
                return jsonify({"error": "No 'audio' file in request"}), 400
            audio_file = request.files["audio"]
            audio_data = audio_file.read()
        else:
            # Handle raw audio in body
            audio_data = request.get_data()

        if not audio_data or len(audio_data) < 44:
            return jsonify({"error": "No audio data received or data too short"}), 400

        # Validate WAV header
        if audio_data[:4] != b'RIFF' or audio_data[8:12] != b'WAVE':
            return jsonify({"error": "Invalid WAV format. Expected RIFF/WAVE header."}), 400

        # Write to temp file (faster-whisper needs a file path)
        with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
            temp_path = f.name
            f.write(audio_data)

        try:
            # Load model and transcribe
            model = get_model()

            segments, info = model.transcribe(
                temp_path,
                beam_size=5,
                vad_filter=True,  # Filter out silence
                vad_parameters=dict(
                    min_silence_duration_ms=500,
                    speech_pad_ms=200
                )
            )

            # Collect all segments
            segment_list = []
            full_text = []

            for segment in segments:
                segment_list.append({
                    "start": segment.start,
                    "end": segment.end,
                    "text": segment.text.strip()
                })
                full_text.append(segment.text.strip())

            transcribed_text = " ".join(full_text).strip()
            processing_time = time.time() - start_time

            # Log the transcription
            print(f"Transcribed ({processing_time:.2f}s): \"{transcribed_text[:100]}{'...' if len(transcribed_text) > 100 else ''}\"")

            return jsonify({
                "text": transcribed_text,
                "language": info.language,
                "language_probability": info.language_probability,
                "duration": info.duration,
                "processing_time": processing_time,
                "segments": segment_list
            })

        finally:
            # Clean up temp file
            if os.path.exists(temp_path):
                os.unlink(temp_path)

    except ImportError as e:
        return jsonify({
            "error": "faster-whisper not installed",
            "details": str(e),
            "install": "pip install faster-whisper"
        }), 500
    except Exception as e:
        print(f"Transcription error: {e}")
        return jsonify({"error": str(e)}), 500


def main():
    global MODEL_NAME, DEVICE, COMPUTE_TYPE

    parser = argparse.ArgumentParser(description="Whisper STT HTTP Server")
    parser.add_argument("--port", type=int, default=5051, help="Port to listen on")
    parser.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    parser.add_argument("--model", type=str, default="base.en",
                        help="Whisper model to use (tiny, base, small, medium, large)")
    parser.add_argument("--device", type=str, default="auto",
                        choices=["auto", "cuda", "cpu"],
                        help="Device to run on")
    parser.add_argument("--compute-type", type=str, default="auto",
                        help="Compute type (auto, float16, int8, etc.)")
    parser.add_argument("--preload", action="store_true",
                        help="Load model on startup instead of first request")
    args = parser.parse_args()

    MODEL_NAME = args.model
    DEVICE = args.device
    COMPUTE_TYPE = args.compute_type

    print(f"Whisper STT Server")
    print(f"  Model: {MODEL_NAME}")
    print(f"  Device: {DEVICE}")
    print(f"  Compute type: {COMPUTE_TYPE}")

    # Optionally preload the model
    if args.preload:
        try:
            get_model()
        except ImportError:
            print("Error: faster-whisper not installed")
            print("Install with: pip install faster-whisper")
            sys.exit(1)
        except Exception as e:
            print(f"Error loading model: {e}")
            sys.exit(1)

    print(f"\nStarting server on http://{args.host}:{args.port}")
    print(f"Endpoints:")
    print(f"  GET  /health     - Health check")
    print(f"  GET  /models     - List models")
    print(f"  POST /transcribe - Speech to text")
    print(f"\nTest with:")
    print(f"  curl http://localhost:{args.port}/health")
    print(f"  curl -X POST http://localhost:{args.port}/transcribe \\")
    print(f"       -H 'Content-Type: audio/wav' --data-binary @recording.wav")

    app.run(host=args.host, port=args.port, debug=False, threaded=True)


if __name__ == "__main__":
    main()
