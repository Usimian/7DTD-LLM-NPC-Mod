@echo off
REM ========================================
REM  Package NPCLLMChat - Simple Release
REM ========================================

set VERSION=1.1.0
set RELEASE_NAME=NPCLLMChat-v%VERSION%

echo Packaging NPCLLMChat v%VERSION%...

REM Clean old release
if exist "%RELEASE_NAME%.zip" del "%RELEASE_NAME%.zip"
if exist "_package" rmdir /s /q "_package"

REM Create temp folder structure
mkdir "_package\NPCLLMChat"

REM Copy mod files
copy /Y "NPCLLMChat\bin\Release\NPCLLMChat.dll" "_package\NPCLLMChat\"
copy /Y "NPCLLMChat\ModInfo.xml" "_package\NPCLLMChat\"
xcopy /Y /E /I "NPCLLMChat\Config" "_package\NPCLLMChat\Config"

REM Copy servers with venvs (no Python install needed)
xcopy /Y /E /I "piper-server" "_package\NPCLLMChat\piper-server"
xcopy /Y /E /I "whisper-server" "_package\NPCLLMChat\whisper-server"

REM Create simple README
(
echo NPCLLMChat - AI NPC Conversations
echo =====================================
echo.
echo TWO MODES AVAILABLE:
echo.
echo TEXT-ONLY MODE ^(Recommended for beginners^):
echo   - Chat with NPCs using @message in game chat
echo   - Responses appear on screen
echo   - Only requires Ollama ^(no Python needed^)
echo.
echo VOICE MODE ^(Optional^):
echo   - Speak to NPCs using V key
echo   - NPCs respond with voice
echo   - Requires Python 3.10+ installed
echo.
echo =====================================
echo INSTALLATION - TEXT-ONLY MODE:
echo =====================================
echo.
echo 1. Install required mods:
echo    - 0-SCore mod ^(must match game version^)
echo    - 0-NPCCore mod
echo.
echo 2. Install Ollama:
echo    - Download from ollama.com
echo    - Run: ollama pull gemma3:4b
echo.
echo 3. Extract this ZIP to your Mods folder:
echo    Right-click ZIP ^> Extract All ^> Browse to Mods folder
echo.
echo 4. Launch game and talk to NPCs:
echo    Type: @Hello there!
echo    ^(Must be within 5m of an NPC^)
echo.
echo =====================================
echo OPTIONAL - VOICE MODE SETUP:
echo =====================================
echo.
echo 1. Install Python 3.10 or newer:
echo    - Download from python.org
echo    - CHECK "Add Python to PATH" during install
echo.
echo 2. Voice features will auto-start:
echo    - TTS/STT servers start automatically
echo    - Hold V key to speak to NPCs
echo    - NPCs will respond with voice
echo.
echo NOTE: Python packages are pre-bundled in this release!
echo No pip install or setup needed - just install Python.
echo.
echo =====================================
echo USAGE:
echo =====================================
echo.
echo Text Chat:  @Hello, who are you?
echo Commands:   @Follow me, @Stay here, @Guard this area
echo Voice:      Hold V key, speak, release ^(requires Python^)
echo.
echo =====================================
) > "_package\NPCLLMChat\README.txt"

REM Zip it
powershell Compress-Archive -Path "_package\NPCLLMChat" -DestinationPath "%RELEASE_NAME%.zip" -Force

REM Cleanup
rmdir /s /q "_package"

echo.
echo Done! Created: %RELEASE_NAME%.zip
pause
