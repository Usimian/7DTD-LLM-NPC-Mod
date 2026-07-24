using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace NPCLLMChat
{
    /// <summary>
    /// Manages automatic startup of TTS and STT servers
    /// </summary>
    public static class ServerManager
    {
        private static Process piperProcess;
        private static Process whisperProcess;
        private static bool serversStarted = false;

        public static void StartServers()
        {
            if (serversStarted) return;
            serversStarted = true;

            // Get the mod directory
            string modPath = GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                Log.Warning("[NPCLLMChat] ServerManager: Could not determine mod path");
                return;
            }

            Log.Out($"[NPCLLMChat] ServerManager: Mod path = {modPath}");

            // Check if Ollama is running (don't auto-start - causes Steam hang issues)
            CheckOllamaStatus();

            // A server already listening (e.g. run as a systemd user service, or surviving
            // a game restart) is used as-is; only spawn our own when the port is free.
            // Under Steam's Linux runtime the game can't exec host Python at all, so
            // externally-managed servers are the normal case there.
            if (IsPortListening(5050))
                Log.Out("[NPCLLMChat] ServerManager: Piper TTS already running on port 5050, using it");
            else
                StartPiperServer(modPath);

            if (IsPortListening(5051))
                Log.Out("[NPCLLMChat] ServerManager: Whisper STT already running on port 5051, using it");
            else
                StartWhisperServer(modPath);
        }

        private static bool IsPortListening(int port)
        {
            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                    return success && client.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void StartPiperServer(string modPath)
        {
            try
            {
                // Try to find piper-server in multiple locations
                string piperDir = FindServerDirectory("piper-server", modPath);
                if (piperDir == null)
                {
                    Log.Warning("[NPCLLMChat] ServerManager: piper-server not found. Install alongside the mod or set NPCLLM_SERVERS_PATH");
                    return;
                }

                string piperScript = Path.Combine(piperDir, "piper_server.py");
                if (!File.Exists(piperScript))
                {
                    Log.Warning($"[NPCLLMChat] ServerManager: piper_server.py not found at {piperScript}");
                    return;
                }

                // Find Python executable and site-packages
                string pythonExe;
                string sitePackages;
                if (!FindPythonEnvironment(piperDir, out pythonExe, out sitePackages))
                {
                    Log.Warning("[NPCLLMChat] ServerManager: Python not found. Please install Python 3.9+");
                    return;
                }

                Log.Out($"[NPCLLMChat] ServerManager: Starting Piper TTS server (using {pythonExe})...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{piperScript}\" --port 5050",
                    WorkingDirectory = piperDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                // Add bundled packages to PYTHONPATH for portability
                if (!string.IsNullOrEmpty(sitePackages))
                {
                    startInfo.EnvironmentVariables["PYTHONPATH"] = sitePackages;
                }

                piperProcess = Process.Start(startInfo);
                Log.Out($"[NPCLLMChat] ServerManager: Piper TTS started (PID: {piperProcess?.Id})");
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] ServerManager: Failed to start Piper: {ex.Message}");
            }
        }

        private static void StartWhisperServer(string modPath)
        {
            try
            {
                // Try to find whisper-server in multiple locations
                string whisperDir = FindServerDirectory("whisper-server", modPath);
                if (whisperDir == null)
                {
                    Log.Warning("[NPCLLMChat] ServerManager: whisper-server not found. Install alongside the mod or set NPCLLM_SERVERS_PATH");
                    return;
                }

                string whisperScript = Path.Combine(whisperDir, "whisper_server.py");
                if (!File.Exists(whisperScript))
                {
                    Log.Warning($"[NPCLLMChat] ServerManager: whisper_server.py not found at {whisperScript}");
                    return;
                }

                // Find Python executable and site-packages
                string pythonExe;
                string sitePackages;
                if (!FindPythonEnvironment(whisperDir, out pythonExe, out sitePackages))
                {
                    Log.Warning("[NPCLLMChat] ServerManager: Python not found. Please install Python 3.9+");
                    return;
                }

                Log.Out($"[NPCLLMChat] ServerManager: Starting Whisper STT server (using {pythonExe})...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{whisperScript}\" --port 5051 --device cpu --compute-type int8 --preload",
                    WorkingDirectory = whisperDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                // Add bundled packages to PYTHONPATH for portability
                if (!string.IsNullOrEmpty(sitePackages))
                {
                    startInfo.EnvironmentVariables["PYTHONPATH"] = sitePackages;
                }

                whisperProcess = Process.Start(startInfo);
                Log.Out($"[NPCLLMChat] ServerManager: Whisper STT started (PID: {whisperProcess?.Id})");
                
                // Wait for Whisper server to be ready (it needs time to load the model)
                Log.Out("[NPCLLMChat] ServerManager: Waiting for Whisper to initialize (loading model)...");
                System.Threading.Thread.Sleep(5000);  // Initial wait for Python to start
                
                // Check if server is responding
                bool whisperReady = false;
                for (int attempt = 0; attempt < 30 && !whisperReady; attempt++)
                {
                    try
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var result = client.BeginConnect("127.0.0.1", 5051, null, null);
                            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                            
                            if (success && client.Connected)
                            {
                                whisperReady = true;
                                client.Close();
                                Log.Out("[NPCLLMChat] ServerManager: Whisper STT is accepting connections!");
                            }
                        }
                    }
                    catch
                    {
                        // Not ready yet
                    }
                    
                    if (!whisperReady && attempt < 29)
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                
                if (!whisperReady)
                {
                    Log.Warning("[NPCLLMChat] ServerManager: Whisper STT failed to start - check if faster-whisper is installed");
                    Log.Warning("[NPCLLMChat] ServerManager: Run setup_servers.bat to install dependencies");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] ServerManager: Failed to start Whisper: {ex.Message}");
            }
        }

        private static void CheckOllamaStatus()
        {
            try
            {
                // Check if Ollama is running by trying to connect
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", 11434, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    
                    if (success && client.Connected)
                    {
                        Log.Out("[NPCLLMChat] ServerManager: Ollama is running");
                        client.Close();
                        return;
                    }
                }
            }
            catch { }

            // Ollama is not running - warn user
            Log.Warning("[NPCLLMChat] ServerManager: Ollama is NOT running!");
            Log.Warning("[NPCLLMChat] ServerManager: NPCs will not respond until Ollama is started.");
            Log.Warning("[NPCLLMChat] ServerManager: Run 'ollama serve' or enable the Ollama service/auto-start.");
        }

        public static void StopServers()
        {
            Log.Out("[NPCLLMChat] ServerManager: Stopping servers...");
            
            // Kill Piper TTS directly (no child processes)
            if (piperProcess != null)
            {
                try
                {
                    if (!piperProcess.HasExited)
                    {
                        piperProcess.Kill();
                        Log.Out("[NPCLLMChat] ServerManager: Piper TTS killed");
                    }
                }
                catch { }
                piperProcess = null;
            }

            // Kill Whisper STT directly (no child processes)
            if (whisperProcess != null)
            {
                try
                {
                    if (!whisperProcess.HasExited)
                    {
                        whisperProcess.Kill();
                        Log.Out("[NPCLLMChat] ServerManager: Whisper STT killed");
                    }
                }
                catch { }
                whisperProcess = null;
            }

            serversStarted = false;
            Log.Out("[NPCLLMChat] ServerManager: Servers stopped.");
        }
        
        private static bool FindPythonEnvironment(string serverDir, out string pythonExe, out string sitePackages)
        {
            pythonExe = null;
            sitePackages = null;

            // On Linux, a venv created in place (setup_servers.sh) has a working interpreter
            // of its own — use it directly, no PYTHONPATH needed. Windows releases bundle only
            // site-packages (a copied venv python.exe wouldn't run), so keep system Python there.
            if (!PlatformHelper.IsWindows)
            {
                string venvPython = Path.Combine(serverDir, "venv", "bin", "python");
                if (File.Exists(venvPython))
                {
                    pythonExe = venvPython;
                    Log.Out($"[NPCLLMChat] ServerManager: Using venv Python at {pythonExe}");
                    return true;
                }
            }

            // Look for bundled site-packages (portable approach)
            string bundledSitePackages = Path.Combine(serverDir, "venv", "Lib", "site-packages");
            if (Directory.Exists(bundledSitePackages))
            {
                sitePackages = bundledSitePackages;
                Log.Out($"[NPCLLMChat] ServerManager: Found bundled packages at {sitePackages}");
            }
            else if (!PlatformHelper.IsWindows)
            {
                // Linux venv layout: venv/lib/python3.X/site-packages
                string libDir = Path.Combine(serverDir, "venv", "lib");
                if (Directory.Exists(libDir))
                {
                    foreach (string pyDir in Directory.GetDirectories(libDir, "python3*"))
                    {
                        string sp = Path.Combine(pyDir, "site-packages");
                        if (Directory.Exists(sp))
                        {
                            sitePackages = sp;
                            Log.Out($"[NPCLLMChat] ServerManager: Found bundled packages at {sitePackages}");
                            break;
                        }
                    }
                }
            }

            // Find Python executable - try multiple locations
            string[] pythonPaths = new[]
            {
                // 1. System Python (most portable - user has Python installed)
                "python",
                "python3",
                // 2. Common Windows Python locations
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "python.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python310", "python.exe"),
                @"C:\Python312\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python310\python.exe",
            };

            foreach (string path in pythonPaths)
            {
                try
                {
                    // Test if this Python works
                    var testProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };

                    testProcess.Start();
                    testProcess.WaitForExit(3000);

                    if (testProcess.ExitCode == 0)
                    {
                        pythonExe = path;
                        Log.Out($"[NPCLLMChat] ServerManager: Found Python at {pythonExe}");
                        return true;
                    }
                }
                catch
                {
                    // This path doesn't work, try next
                }
            }

            return false;
        }

        private static string FindServerDirectory(string serverName, string modPath)
        {
            // Check multiple possible locations for the server directory
            string[] searchPaths = new[]
            {
                // 1. Environment variable (for custom installations)
                Environment.GetEnvironmentVariable("NPCLLM_SERVERS_PATH"),
                
                // 2. Alongside the mod (Mods/NPCLLMChat/piper-server)
                modPath,
                
                // 3. In the Mods folder (Mods/piper-server)
                Path.Combine(modPath, ".."),
                
                // 4. In the game root (same level as Mods)
                Path.Combine(modPath, "..", ".."),
            };

            foreach (string basePath in searchPaths)
            {
                if (string.IsNullOrEmpty(basePath)) continue;

                string serverPath = Path.Combine(basePath, serverName);
                if (Directory.Exists(serverPath))
                {
                    Log.Out($"[NPCLLMChat] ServerManager: Found {serverName} at {serverPath}");
                    return serverPath;
                }
            }

            return null;
        }

        private static string GetModPath()
        {
            // Try to find the mod's DLL location
            try
            {
                // The mod DLL is in: Mods/NPCLLMChat/NPCLLMChat.dll
                // So we want to get the NPCLLMChat folder
                string assemblyLocation = typeof(ServerManager).Assembly.Location;
                if (!string.IsNullOrEmpty(assemblyLocation))
                {
                    return Path.GetDirectoryName(assemblyLocation);
                }
            }
            catch { }

            return null;
        }
    }
}
