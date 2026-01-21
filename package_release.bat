@echo off
REM ========================================
REM  Package NPCLLMChat - Simple Release
REM ========================================

set VERSION=1.0.0
set RELEASE_NAME=NPCLLMChat-v%VERSION%

echo Packaging NPCLLMChat v%VERSION%...

REM Clean old release
if exist "%RELEASE_NAME%.zip" del "%RELEASE_NAME%.zip"
if exist "NPCLLMChat" rmdir /s /q "NPCLLMChat"

REM Create temp folder with exact name for Mods directory
mkdir "NPCLLMChat"

REM Copy everything users need
copy /Y "NPCLLMChat\bin\Release\NPCLLMChat.dll" "NPCLLMChat\"
copy /Y "NPCLLMChat\ModInfo.xml" "NPCLLMChat\"
xcopy /Y /E /I "NPCLLMChat\Config" "NPCLLMChat\Config"
xcopy /Y /E /I "piper-server" "NPCLLMChat\piper-server"
xcopy /Y /E /I "whisper-server" "NPCLLMChat\whisper-server"

REM Create simple README
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
) > "NPCLLMChat\README.txt"

REM Create simple setup script
(
echo @echo off
echo echo Setting up voice servers...
echo cd piper-server
echo pip install -r requirements.txt
echo cd ..\whisper-server
echo if not exist venv python -m venv venv
echo call venv\Scripts\activate.bat
echo pip install -r requirements.txt
echo deactivate
echo cd ..
echo echo Done! Launch the game.
echo pause
) > "NPCLLMChat\setup_servers.bat"

REM Zip it
powershell Compress-Archive -Path "NPCLLMChat" -DestinationPath "%RELEASE_NAME%.zip" -Force

REM Cleanup
rmdir /s /q "NPCLLMChat"

echo.
echo Done! Created: %RELEASE_NAME%.zip
echo Users extract this to their Mods folder.
pause
