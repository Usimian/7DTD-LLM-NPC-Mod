@echo off
REM ========================================
REM  Package NPCLLMChat - Simple Release
REM ========================================

set VERSION=1.0.0
set RELEASE_NAME=NPCLLMChat-v%VERSION%

echo Packaging NPCLLMChat v%VERSION%...

REM Clean old release
if exist "%RELEASE_NAME%.zip" del "%RELEASE_NAME%.zip"
if exist "release" rmdir /s /q "release"

REM Create temp folder structure
mkdir "release\NPCLLMChat"
mkdir "release\NPCLLMChat\piper-server"
mkdir "release\NPCLLMChat\whisper-server"

REM Copy mod files
echo Copying mod files...
copy /Y "NPCLLMChat\bin\Release\NPCLLMChat.dll" "release\NPCLLMChat\" >nul
copy /Y "NPCLLMChat\ModInfo.xml" "release\NPCLLMChat\" >nul
copy /Y "setup_servers.bat" "release\NPCLLMChat\" >nul
xcopy /Y /E /I /Q "NPCLLMChat\Config" "release\NPCLLMChat\Config" >nul

REM Copy piper-server (only source files, no venv)
echo Copying piper-server...
copy /Y "piper-server\*.py" "release\NPCLLMChat\piper-server\" >nul
copy /Y "piper-server\requirements.txt" "release\NPCLLMChat\piper-server\" >nul

REM Copy whisper-server (only source files, no venv)
echo Copying whisper-server...
copy /Y "whisper-server\*.py" "release\NPCLLMChat\whisper-server\" >nul
copy /Y "whisper-server\requirements.txt" "release\NPCLLMChat\whisper-server\" >nul

REM Create simple README
echo Creating README...
(
echo NPCLLMChat - AI NPC Conversations
echo.
echo INSTALLATION:
echo 1. Install prerequisites:
echo    - 0-SCore mod ^(must match game version^)
echo    - 0-NPCCore mod
echo    - Ollama ^(ollama.com^)
echo    - Python 3.9+ ^(python.org^)
echo.
echo 2. Extract this entire NPCLLMChat folder to:
echo    ^<Game^>\Mods\NPCLLMChat\
echo.
echo 3. Run setup_servers.bat ^(one time only^):
echo    cd Mods\NPCLLMChat
echo    setup_servers.bat
echo.
echo 4. Download AI model:
echo    ollama pull gemma3:4b
echo.
echo 5. Launch game and talk to NPCs!
echo    - Text: @Hello
echo    - Voice: Hold V key
) > "release\NPCLLMChat\README.txt"

REM Zip it
echo Creating ZIP...
powershell Compress-Archive -Path "release\NPCLLMChat" -DestinationPath "%RELEASE_NAME%.zip" -Force

REM Cleanup
rmdir /s /q "release"

echo.
echo ========================================
echo Done! Created: %RELEASE_NAME%.zip
echo ========================================
echo.
echo Upload this file to GitHub Releases.
echo Users extract NPCLLMChat folder to their Mods directory.
pause
