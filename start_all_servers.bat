@echo off
REM ========================================
REM  7DTD NPC LLM Chat - Start All Servers
REM ========================================
REM
REM This script starts:
REM  1. Ollama (LLM) - if not already running
REM  2. Whisper (STT) - speech-to-text
REM  3. Piper (TTS) - text-to-speech
REM
REM The servers will run in the background.
REM Use stop_all_servers.bat to stop them.
REM ========================================

echo.
echo ========================================
echo  Starting NPC LLM Chat Servers
echo ========================================
echo.

REM Check if Ollama is already running
echo [1/4] Checking Ollama...
ollama list >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Ollama is already running
    echo   ^> Preloading gemma3:4b model...
    start /B ollama run gemma3:4b "test" >nul 2>&1
) else (
    echo   ^> Starting Ollama service...
    net start OllamaService >nul 2>&1
    timeout /t 3 /nobreak >nul
    echo   ^> Preloading gemma3:4b model...
    start /B ollama run gemma3:4b "test" >nul 2>&1
)
echo.

REM Start Whisper STT server
echo [2/4] Starting Whisper STT server on port 5051...
cd /d "%~dp0whisper-server"
if exist venv\ (
    start "Whisper STT" cmd /k "venv\Scripts\activate && python whisper_server.py --port 5051 --preload"
    echo   ^> Whisper STT starting in new window...
) else (
    echo   ^> ERROR: venv not found. Run: python -m venv venv
    echo   ^> Then: venv\Scripts\activate
    echo   ^> Then: pip install -r requirements.txt
)
cd /d "%~dp0"
echo.

REM Start Piper TTS server
echo [3/4] Starting Piper TTS server on port 5050...
cd /d "%~dp0piper-server"
if exist piper_server.py (
    REM Make sure piper-tts is installed globally
    python -c "import sys; sys.path = [p for p in sys.path if 'venv' not in p.lower()]; import piper" >nul 2>&1
    if %ERRORLEVEL% NEQ 0 (
        echo   ^> Installing piper-tts...
        python -m pip install piper-tts flask numpy >nul 2>&1
    )
    start "Piper TTS" cmd /k "cd /d %~dp0piper-server && python piper_server.py --port 5050"
    echo   ^> Piper TTS starting in new window...
) else (
    echo   ^> ERROR: piper_server.py not found
)
cd /d "%~dp0"
echo.

echo [4/4] Waiting for servers to start...
timeout /t 5 /nobreak >nul
echo.

REM Verify servers are responding
echo ========================================
echo  Server Status Check
echo ========================================
echo.

echo Checking Ollama (http://localhost:11434)...
curl -s http://localhost:11434/api/tags >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Ollama: READY
) else (
    echo   ^> Ollama: NOT RESPONDING
)

echo Checking Whisper (http://localhost:5051)...
curl -s http://localhost:5051/health >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Whisper STT: READY
) else (
    echo   ^> Whisper STT: NOT RESPONDING
)

echo Checking Piper (http://localhost:5050)...
curl -s http://localhost:5050/voices >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Piper TTS: READY
) else (
    echo   ^> Piper TTS: NOT RESPONDING (may still be loading)
)

echo.
echo ========================================
echo  All servers started!
echo  You can now launch 7 Days to Die
echo ========================================
echo.
echo To stop servers: run stop_all_servers.bat
echo.
pause
