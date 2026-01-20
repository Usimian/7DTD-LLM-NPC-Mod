@echo off
REM =====================================================
REM NPC LLM Chat - Start All Servers (Windows)
REM =====================================================
REM This script starts the optional enhanced TTS and STT servers.
REM These are OPTIONAL - the mod works without them using Windows built-in voices.
REM
REM Requirements:
REM   - Python 3.10+ with pip
REM   - Voice models downloaded for Piper
REM   - Dependencies installed (see below)
REM
REM Install dependencies first:
REM   cd piper-server && pip install -r requirements.txt
REM   cd whisper-server && pip install -r requirements.txt
REM =====================================================

echo.
echo ========================================
echo   NPC LLM Chat - Starting Audio Servers
echo ========================================
echo.

REM Get script directory
set SCRIPT_DIR=%~dp0

REM Check if Python is available
where python >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] Python not found in PATH!
    echo Install Python from https://python.org and add to PATH
    pause
    exit /b 1
)

echo Starting Piper TTS Server (port 5050)...
start "Piper TTS Server" cmd /k "cd /d %SCRIPT_DIR%piper-server && python piper_server.py --port 5050"

echo Starting Whisper STT Server (port 5051)...
start "Whisper STT Server" cmd /k "cd /d %SCRIPT_DIR%whisper-server && .\venv\Scripts\activate && python whisper_server.py --port 5051 --preload"

echo.
echo ========================================
echo   Servers starting in new windows...
echo ========================================
echo.
echo   TTS: http://localhost:5050/health
echo   STT: http://localhost:5051/health
echo.
echo Close this window when done. Server windows will stay open.
echo To stop servers, close their respective windows.
echo.
pause
