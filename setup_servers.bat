@echo off
REM ===================================================================
REM NPC LLM Chat - voice server setup
REM
REM Run this ONCE from the NPCLLMChat folder inside your Mods folder,
REM before launching the game. It builds the Python environments the
REM voice features need. Text chat works without it.
REM ===================================================================

echo ========================================
echo NPC LLM Chat - Server Setup
echo ========================================
echo.

if not exist "piper-server" if not exist "whisper-server" (
    echo ERROR: piper-server and whisper-server are not in this folder.
    echo Run this script from the folder that contains them - for an
    echo installed mod that is:
    echo   ...\7 Days To Die\Mods\NPCLLMChat\setup_servers.bat
    echo.
    pause
    exit /b 1
)

python --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH
    echo Please install Python 3.10+ from https://www.python.org/downloads/
    echo Make sure to check "Add Python to PATH" during installation
    echo.
    pause
    exit /b 1
)

echo Found Python:
python --version
echo.

call :setup_server "piper-server" "Piper TTS" "piper-tts"
call :setup_server "whisper-server" "Whisper STT" ""

echo ========================================
echo Setup Complete!
echo ========================================
echo.
echo Next steps:
echo 1. Make sure Ollama is installed: https://ollama.com/download
echo 2. Download an AI model: ollama pull gemma3:4b
echo 3. Launch 7 Days to Die
echo.
echo The mod starts the voice servers automatically when the game loads.
echo.
pause
exit /b 0


:setup_server
set "SRVDIR=%~1"
set "SRVNAME=%~2"
set "EXTRA=%~3"

echo ========================================
echo Setting up %SRVNAME% server...
echo ========================================

if not exist "%SRVDIR%" (
    echo WARNING: %SRVDIR% folder not found, skipping...
    echo.
    goto :eof
)

pushd "%SRVDIR%"

REM A virtual environment records the absolute path of the Python that built it,
REM so one created on somebody else's machine can never run on yours. Test the
REM one that is here, and replace it if it does not work - installing into a
REM broken venv leaves it just as broken.
if exist "venv" (
    venv\Scripts\python.exe -c "import sys" >nul 2>&1
    if errorlevel 1 (
        echo Existing environment was built elsewhere and cannot run here.
        echo Removing it and building a fresh one...
        rmdir /s /q venv
    )
)

if not exist "venv" (
    echo Creating virtual environment...
    python -m venv venv
    if errorlevel 1 (
        echo ERROR: could not create the virtual environment for %SRVNAME%.
        popd
        echo.
        goto :eof
    )
)

echo Installing %SRVNAME% dependencies...
call venv\Scripts\activate.bat
python -m pip install --upgrade pip --quiet
pip install -r requirements.txt
if not "%EXTRA%"=="" pip install %EXTRA%
call deactivate

echo %SRVNAME% setup complete!
popd
echo.
goto :eof
