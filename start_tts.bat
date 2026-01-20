@echo off
REM =====================================================
REM NPC LLM Chat - Start Piper TTS Server (Windows)
REM =====================================================
REM This is OPTIONAL - the mod works without it using Windows SAPI voices.
REM Piper provides higher quality, more natural sounding voices.
REM
REM Setup (one-time):
REM   1. cd piper-server
REM   2. pip install -r requirements.txt
REM   3. Download voice models to %USERPROFILE%\.local\share\piper\voices\
REM =====================================================

echo.
echo ========================================
echo   Piper TTS Server
echo ========================================
echo.

cd /d %~dp0piper-server

REM Check for requirements
if not exist "piper_server.py" (
    echo [ERROR] piper_server.py not found!
    echo Make sure you're in the correct directory.
    pause
    exit /b 1
)

echo Starting server on http://localhost:5050
echo Press Ctrl+C to stop
echo.

python piper_server.py --port 5050

pause
