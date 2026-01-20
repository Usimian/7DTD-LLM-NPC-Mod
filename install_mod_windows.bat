@echo off
REM ========================================
REM  Install NPCLLMChat Mod to 7 Days to Die
REM ========================================

REM Try to auto-detect Steam installation
set "GAME_PATH="

REM Check common Steam library locations
if exist "C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die" (
    set "GAME_PATH=C:\Program Files (x86)\Steam\steamapps\common\7 Days To Die"
) else if exist "C:\Program Files\Steam\steamapps\common\7 Days To Die" (
    set "GAME_PATH=C:\Program Files\Steam\steamapps\common\7 Days To Die"
) else if exist "D:\SteamLibrary\steamapps\common\7 Days To Die" (
    set "GAME_PATH=D:\SteamLibrary\steamapps\common\7 Days To Die"
) else if exist "E:\SteamLibrary\steamapps\common\7 Days To Die" (
    set "GAME_PATH=E:\SteamLibrary\steamapps\common\7 Days To Die"
)

REM If not found, ask user
if "%GAME_PATH%"=="" (
    echo Could not auto-detect 7 Days to Die installation.
    echo.
    set /p "GAME_PATH=Enter the full path to your 7 Days to Die folder: "
)

REM Verify path exists
if not exist "%GAME_PATH%" (
    echo ERROR: Game path does not exist: %GAME_PATH%
    echo.
    pause
    exit /b 1
)

set MOD_PATH=%GAME_PATH%\Mods\NPCLLMChat

echo.
echo ========================================
echo  Installing NPCLLMChat Mod
echo ========================================
echo Game Path: %GAME_PATH%
echo Mod Path:  %MOD_PATH%
echo.

REM Create mod directory
if not exist "%MOD_PATH%" mkdir "%MOD_PATH%"

REM Copy the compiled DLL
echo [1/4] Copying mod DLL...
copy /Y "NPCLLMChat\bin\Release\NPCLLMChat.dll" "%MOD_PATH%\"

REM Copy config files
echo [2/4] Copying config files...
xcopy /Y /E /I "NPCLLMChat\Config" "%MOD_PATH%\Config"

REM Copy ModInfo
echo [3/4] Copying ModInfo.xml...
copy /Y "NPCLLMChat\ModInfo.xml" "%MOD_PATH%\"

REM Copy server folders to mod directory
echo [4/4] Copying server folders...
xcopy /Y /E /I "piper-server" "%MOD_PATH%\piper-server"
xcopy /Y /E /I "whisper-server" "%MOD_PATH%\whisper-server"

echo.
echo ========================================
echo  Installation Complete!
echo ========================================
echo.
echo Everything installed to: %MOD_PATH%
echo   - NPCLLMChat.dll
echo   - Config/
echo   - piper-server/
echo   - whisper-server/
echo.
echo The mod will auto-start Piper and Whisper when you launch the game!
echo.
pause
