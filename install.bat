@echo off
REM ========================================
REM  NPCLLMChat - Automated Installation
REM ========================================

echo ========================================
echo NPCLLMChat Mod - Installation Script
echo ========================================
echo.

REM Check if game path is provided
if "%~1"=="" (
    echo ERROR: Game path not provided
    echo.
    echo Usage: install.bat "C:\Path\To\7 Days To Die"
    echo Example: install.bat "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
    echo.
    pause
    exit /b 1
)

set GAME_PATH=%~1
set MODS_PATH=%GAME_PATH%\Mods\NPCLLMChat

echo Game Path: %GAME_PATH%
echo Mods Path: %MODS_PATH%
echo.

REM Verify game path exists
if not exist "%GAME_PATH%" (
    echo ERROR: Game path does not exist: %GAME_PATH%
    pause
    exit /b 1
)

REM Check if DLL exists (mod is built)
if not exist "NPCLLMChat\bin\Release\NPCLLMChat.dll" (
    echo ERROR: NPCLLMChat.dll not found!
    echo Please build the mod first:
    echo   dotnet build NPCLLMChat\NPCLLMChat.csproj -c Release
    echo.
    pause
    exit /b 1
)

REM Create Mods directory if it doesn't exist
if not exist "%GAME_PATH%\Mods" mkdir "%GAME_PATH%\Mods"

REM Create mod directory
echo Creating mod directory...
if exist "%MODS_PATH%" (
    echo Removing old installation...
    rmdir /s /q "%MODS_PATH%"
)
mkdir "%MODS_PATH%"

REM Copy mod files
echo.
echo Installing mod files...

echo   - Copying NPCLLMChat.dll...
copy /Y "NPCLLMChat\bin\Release\NPCLLMChat.dll" "%MODS_PATH%\"

echo   - Copying ModInfo.xml...
copy /Y "NPCLLMChat\ModInfo.xml" "%MODS_PATH%\"

echo   - Copying Config folder...
xcopy /Y /E /I "NPCLLMChat\Config" "%MODS_PATH%\Config"

echo   - Copying piper-server...
xcopy /Y /E /I "piper-server" "%MODS_PATH%\piper-server"

echo   - Copying whisper-server...
xcopy /Y /E /I "whisper-server" "%MODS_PATH%\whisper-server"

echo   - Copying setup_servers.bat...
copy /Y "setup_servers.bat" "%MODS_PATH%\"

echo.
echo ========================================
echo Installation Complete!
echo ========================================
echo.
echo Mod installed to: %MODS_PATH%
echo.
echo Next steps:
echo 1. Run setup for voice features:
echo    cd "%MODS_PATH%"
echo    setup_servers.bat
echo.
echo 2. Download AI model:
echo    ollama pull gemma3:4b
echo.
echo 3. Launch 7 Days to Die!
echo.
pause
