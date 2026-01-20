@echo off
REM ========================================
REM  7DTD NPC LLM Chat - Stop All Servers
REM ========================================

echo.
echo ========================================
echo  Stopping NPC LLM Chat Servers
echo ========================================
echo.

echo [1/3] Stopping Whisper STT server...
taskkill /FI "WINDOWTITLE eq Whisper STT*" /T /F >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Whisper STT stopped
) else (
    echo   ^> Whisper STT not running
)

echo [2/3] Stopping Piper TTS server...
taskkill /FI "WINDOWTITLE eq Piper TTS*" /T /F >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo   ^> Piper TTS stopped
) else (
    echo   ^> Piper TTS not running
)

echo [3/3] Ollama service...
echo   ^> Note: Ollama runs as Windows service - not stopping
echo   ^> To stop manually: net stop OllamaService

echo.
echo ========================================
echo  Servers stopped!
echo ========================================
echo.
pause
