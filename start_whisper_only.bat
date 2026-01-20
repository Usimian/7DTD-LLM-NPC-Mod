@echo off
echo Starting Whisper STT Server...
echo.
cd /d "%~dp0whisper-server"
call venv\Scripts\activate
python whisper_server.py --port 5051 --device cpu --compute-type int8 --preload
pause
