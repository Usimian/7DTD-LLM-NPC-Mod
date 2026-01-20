@echo off
echo Starting Piper TTS Server...
echo.
cd /d "%~dp0piper-server"
python piper_server.py --port 5050
pause
