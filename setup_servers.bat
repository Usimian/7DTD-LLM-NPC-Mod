@echo off
REM Setup script for NPC LLM Chat mod
REM Installs Python dependencies for TTS and STT servers

echo ========================================
echo NPC LLM Chat - Server Setup
echo ========================================
echo.

REM Check if Python is installed
python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH
    echo Please install Python 3.9+ from https://www.python.org/downloads/
    echo Make sure to check "Add to PATH" during installation
    pause
    exit /b 1
)

echo Found Python:
python --version
echo.

REM Setup Piper TTS server
echo ========================================
echo Setting up Piper TTS server...
echo ========================================
if exist "piper-server" (
    cd piper-server
    
    REM Create virtual environment if it doesn't exist
    if not exist "venv" (
        echo Creating virtual environment...
        python -m venv venv
    )
    
    REM Install dependencies
    echo Installing Piper TTS dependencies...
    call venv\Scripts\activate.bat
    python -m pip install --upgrade pip --quiet
    pip install -r requirements.txt
    pip install piper-tts
    call deactivate
    
    echo Piper TTS setup complete!
    cd ..
) else (
    echo WARNING: piper-server directory not found, skipping...
)
echo.

REM Setup Whisper STT server
echo ========================================
echo Setting up Whisper STT server...
echo ========================================
if exist "whisper-server" (
    cd whisper-server
    
    REM Create virtual environment if it doesn't exist
    if not exist "venv" (
        echo Creating virtual environment...
        python -m venv venv
    )
    
    REM Install dependencies
    echo Installing Whisper STT dependencies...
    call venv\Scripts\activate.bat
    python -m pip install --upgrade pip --quiet
    pip install -r requirements.txt
    call deactivate
    
    echo Whisper STT setup complete!
    cd ..
) else (
    echo WARNING: whisper-server directory not found, skipping...
)
echo.

echo ========================================
echo Setup Complete!
echo ========================================
echo.
echo The mod is now ready to use.
echo.
echo Next steps:
echo 1. Make sure Ollama is installed: https://ollama.com/download
echo 2. Download an AI model: ollama pull gemma3:4b
echo 3. Launch 7 Days to Die
echo.
echo The mod will automatically start all servers when the game loads.
echo.
pause
