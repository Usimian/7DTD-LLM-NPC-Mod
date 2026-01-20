@echo off
REM =====================================================
REM NPC LLM Chat - Ollama Setup (Windows)
REM =====================================================
REM This script helps set up Ollama for the LLM component.
REM Ollama is REQUIRED for the mod to work.
REM =====================================================

echo.
echo ========================================
echo   NPC LLM Chat - Ollama Setup
echo ========================================
echo.

REM Check if Ollama is installed
where ollama >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [!] Ollama not found!
    echo.
    echo Please install Ollama from: https://ollama.ai/download
    echo After installing, run this script again.
    echo.
    start https://ollama.ai/download
    pause
    exit /b 1
)

echo [OK] Ollama is installed
ollama --version
echo.

REM Check if Ollama is running
curl -s http://localhost:11434/api/tags >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [!] Ollama server not running. Starting...
    start "" ollama serve
    timeout /t 3 >nul
)

echo.
echo ========================================
echo   Choose a Model to Download
echo ========================================
echo.
echo Select based on your GPU VRAM:
echo.
echo   1) llama3.2:1b   - Low-end GPU (6-8GB VRAM) - Fast but basic
echo   2) llama3.2:3b   - Mid-range GPU (8-12GB VRAM) - Recommended
echo   3) gemma2:2b     - Alternative fast model
echo   4) mistral       - Good quality, ~7B params
echo   5) Skip          - I already have a model
echo.

set /p CHOICE="Enter choice [1-5]: "

if "%CHOICE%"=="1" (
    echo.
    echo Downloading llama3.2:1b...
    ollama pull llama3.2:1b
    set MODEL=llama3.2:1b
) else if "%CHOICE%"=="2" (
    echo.
    echo Downloading llama3.2:3b...
    ollama pull llama3.2:3b
    set MODEL=llama3.2:3b
) else if "%CHOICE%"=="3" (
    echo.
    echo Downloading gemma2:2b...
    ollama pull gemma2:2b
    set MODEL=gemma2:2b
) else if "%CHOICE%"=="4" (
    echo.
    echo Downloading mistral...
    ollama pull mistral
    set MODEL=mistral
) else (
    echo Skipping model download.
    goto :test
)

:test
echo.
echo ========================================
echo   Testing Ollama
echo ========================================
echo.

REM List installed models
echo Installed models:
ollama list

echo.
echo Testing with a quick prompt...
echo.

if defined MODEL (
    ollama run %MODEL% "Say 'Ready to survive!' and nothing else"
) else (
    echo Skipped test - no model selected.
)

echo.
echo ========================================
echo   Setup Complete!
echo ========================================
echo.
echo Ollama is ready. It runs in the background automatically.
echo.
echo Next steps:
echo   1. Install the NPCLLMChat mod to your 7DTD Mods folder
echo   2. Start 7 Days to Die
echo   3. Talk to NPCs using @message in chat
echo.
echo To test Ollama: curl http://localhost:11434/api/tags
echo.
pause
