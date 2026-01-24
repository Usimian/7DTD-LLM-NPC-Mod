@echo off
REM ========================================
REM  Package NPCLLMChat - Simple Release
REM ========================================

set VERSION=1.0.0
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
echo.
echo INSTALLATION:
echo 1. Install prerequisites:
echo    - 0-SCore mod ^(must match game version^)
echo    - 0-NPCCore mod
echo    - Ollama ^(ollama.com^)
echo    - Python 3.10+ ^(python.org^) - Check "Add to PATH"
echo.
echo 2. Extract to your Mods folder:
echo    Right-click ZIP ^> Extract All ^> Browse to Mods folder
echo.
echo 3. Download AI model:
echo    ollama pull gemma3:4b
echo.
echo 4. Launch game and talk to NPCs!
echo    - Text: @Hello
echo    - Voice: Hold V key
echo.
echo NOTE: Python packages are pre-bundled - no pip install needed!
) > "_package\NPCLLMChat\README.txt"

REM Zip it
powershell Compress-Archive -Path "_package\NPCLLMChat" -DestinationPath "%RELEASE_NAME%.zip" -Force

REM Cleanup
rmdir /s /q "_package"

echo.
echo Done! Created: %RELEASE_NAME%.zip
pause
