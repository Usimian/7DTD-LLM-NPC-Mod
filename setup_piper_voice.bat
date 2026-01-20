@echo off
REM Download a default Piper voice model for Windows

echo.
echo ========================================
echo  Piper TTS - Download Voice Model
echo ========================================
echo.

set VOICES_DIR=%USERPROFILE%\AppData\Local\piper\voices

echo Creating voices directory...
if not exist "%VOICES_DIR%" mkdir "%VOICES_DIR%"

echo.
echo Downloading en_US-lessac-medium voice model...
echo This is a high-quality American English voice (~50MB)
echo.

cd /d "%VOICES_DIR%"

REM Download the .onnx model file
echo [1/2] Downloading model file...
curl -L -o en_US-lessac-medium.onnx "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx"

REM Download the .onnx.json config file
echo [2/2] Downloading config file...
curl -L -o en_US-lessac-medium.onnx.json "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json"

echo.
echo ========================================
echo  Voice model installed!
echo  Location: %VOICES_DIR%
echo ========================================
echo.
echo You can now start the Piper server:
echo   python piper_server.py --port 5050
echo.
pause
