@echo off
REM =====================================================
REM NPC LLM Chat - Stop All Servers (Windows)
REM =====================================================

echo.
echo ========================================
echo   Stopping NPC LLM Chat Servers
echo ========================================
echo.

REM Kill Python processes running our servers
echo Looking for piper_server.py processes...
for /f "tokens=2" %%i in ('tasklist /fi "imagename eq python.exe" /fo csv ^| findstr /i "piper_server"') do (
    echo Killing PID %%i
    taskkill /PID %%i /F 2>nul
)

echo Looking for whisper_server.py processes...
for /f "tokens=2" %%i in ('tasklist /fi "imagename eq python.exe" /fo csv ^| findstr /i "whisper_server"') do (
    echo Killing PID %%i
    taskkill /PID %%i /F 2>nul
)

REM Alternative: kill by window title
taskkill /FI "WINDOWTITLE eq Piper TTS Server*" /F 2>nul
taskkill /FI "WINDOWTITLE eq Whisper STT Server*" /F 2>nul

echo.
echo Done. You can also just close the server windows manually.
echo.
pause
