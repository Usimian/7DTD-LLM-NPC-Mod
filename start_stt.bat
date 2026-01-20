@echo off
REM =====================================================
REM NPC LLM Chat - Start Whisper STT Server (Windows)
REM =====================================================
REM This is OPTIONAL - the mod works without it using Windows Speech Recognition.
REM Whisper provides more accurate speech recognition.
REM
REM Setup (one-time):
REM   1. cd whisper-server
REM   2. python -m venv venv
REM   3. .\venv\Scripts\activate
REM   4. pip install -r requirements.txt
REM =====================================================

echo.
echo ========================================
echo   Whisper STT Server  
echo ========================================
echo.

cd /d %~dp0whisper-server

REM Check for virtual environment
if not exist "venv\Scripts\activate.bat" (
    echo [ERROR] Virtual environment not found!
    echo.
    echo Run these commands first:
    echo   cd whisper-server
    echo   python -m venv venv
    echo   .\venv\Scripts\activate
    echo   pip install -r requirements.txt
    echo.
    pause
    exit /b 1
)

echo Activating virtual environment...
call venv\Scripts\activate.bat

echo Starting server on http://localhost:5051
echo Model will be downloaded on first run (~150MB for base.en)
echo Press Ctrl+C to stop
echo.

python whisper_server.py --port 5051 --preload

pause
