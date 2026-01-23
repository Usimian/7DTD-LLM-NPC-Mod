using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace NPCLLMChat
{
    /// <summary>
    /// Manages automatic startup of TTS and STT servers on Windows
    /// </summary>
    public static class ServerManager
    {
        private static Process piperProcess;
        private static Process whisperProcess;
        private static Process ollamaProcess;
        private static bool serversStarted = false;

        public static void StartServers()
        {
            if (serversStarted) return;
            serversStarted = true;

            if (!PlatformHelper.IsWindows)
            {
                Log.Out("[NPCLLMChat] ServerManager: Not on Windows, skipping auto-start");
                return;
            }

            // Get the mod directory
            string modPath = GetModPath();
            if (string.IsNullOrEmpty(modPath))
            {
                Log.Warning("[NPCLLMChat] ServerManager: Could not determine mod path");
                return;
            }

            Log.Out($"[NPCLLMChat] ServerManager: Mod path = {modPath}");

            // Start Ollama if not already running
            StartOllamaIfNeeded();

            // Kill any existing servers on our ports
            KillProcessOnPort(5050, "Piper TTS");
            KillProcessOnPort(5051, "Whisper STT");

            // Start Piper TTS
            StartPiperServer(modPath);

            // Start Whisper STT
            StartWhisperServer(modPath);
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

                Log.Out("[NPCLLMChat] ServerManager: Starting Piper TTS server...");

                piperProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python",
                        Arguments = $"\"{piperScript}\" --port 5050",
                        WorkingDirectory = piperDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    }
                };

                piperProcess.Start();
                Log.Out($"[NPCLLMChat] ServerManager: Piper TTS started (PID: {piperProcess.Id})");
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
                string venvPython = Path.Combine(whisperDir, "venv", "Scripts", "python.exe");

                if (!File.Exists(whisperScript))
                {
                    Log.Warning($"[NPCLLMChat] ServerManager: whisper_server.py not found at {whisperScript}");
                    return;
                }

                // Use venv Python if available, otherwise system Python
                string pythonExe = File.Exists(venvPython) ? venvPython : "python";

                Log.Out($"[NPCLLMChat] ServerManager: Starting Whisper STT server (using {pythonExe})...");

                whisperProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = pythonExe,
                        Arguments = $"\"{whisperScript}\" --port 5051 --device cpu --compute-type int8 --preload",
                        WorkingDirectory = whisperDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    }
                };

                whisperProcess.Start();
                Log.Out($"[NPCLLMChat] ServerManager: Whisper STT started (PID: {whisperProcess.Id})");
                
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

        private static void StartOllamaIfNeeded()
        {
            try
            {
                // Check if Ollama is already running by trying to connect
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    var result = client.BeginConnect("127.0.0.1", 11434, null, null);
                    var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));
                    
                    if (success && client.Connected)
                    {
                        Log.Out("[NPCLLMChat] ServerManager: Ollama is already running");
                        client.Close();
                        return;
                    }
                }
            }
            catch
            {
                // Ollama is not running, we'll start it
            }

            try
            {
                Log.Out("[NPCLLMChat] ServerManager: Starting Ollama...");
                
                // Start Ollama via PowerShell in a completely detached way
                // This ensures it runs with proper environment and doesn't get blocked by I/O redirection
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-WindowStyle Hidden -Command \"Start-Process -FilePath 'ollama' -ArgumentList 'serve' -WindowStyle Hidden -PassThru\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                var psProcess = Process.Start(startInfo);
                if (psProcess != null)
                {
                    // Wait for PowerShell to complete starting Ollama
                    psProcess.WaitForExit(3000);
                    Log.Out("[NPCLLMChat] ServerManager: Ollama start command executed");
                }
                
                // Wait for Ollama to initialize - be generous with time
                Log.Out("[NPCLLMChat] ServerManager: Waiting for Ollama to initialize...");
                System.Threading.Thread.Sleep(10000);  // 10 second initial wait
                
                // Verify it's actually responding to port connections
                bool connected = false;
                for (int attempt = 0; attempt < 20 && !connected; attempt++)
                {
                    try
                    {
                        using (var client = new System.Net.Sockets.TcpClient())
                        {
                            var result = client.BeginConnect("127.0.0.1", 11434, null, null);
                            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(1));
                            
                            if (success && client.Connected)
                            {
                                connected = true;
                                client.Close();
                                Log.Out("[NPCLLMChat] ServerManager: Ollama is accepting connections!");
                            }
                        }
                    }
                    catch
                    {
                        // Not ready yet
                    }
                    
                    if (!connected && attempt < 19)
                    {
                        System.Threading.Thread.Sleep(1000);
                    }
                }
                
                if (!connected)
                {
                    Log.Warning("[NPCLLMChat] ServerManager: Ollama failed to start properly");
                    Log.Warning("[NPCLLMChat] ServerManager: Please ensure Ollama is installed and in your PATH");
                    return;
                }
                
                // Additional wait to let Ollama fully initialize its model loading capability
                Log.Out("[NPCLLMChat] ServerManager: Allowing Ollama time to fully initialize...");
                System.Threading.Thread.Sleep(5000);
                
                Log.Out("[NPCLLMChat] ServerManager: Ollama startup complete!");
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] ServerManager: Could not auto-start Ollama: {ex.Message}");
                Log.Warning("[NPCLLMChat] ServerManager: Please start Ollama manually: ollama serve");
            }
        }

        public static void StopServers()
        {
            if (piperProcess != null && !piperProcess.HasExited)
            {
                try
                {
                    Log.Out("[NPCLLMChat] ServerManager: Stopping Piper TTS...");
                    piperProcess.Kill();
                    piperProcess.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[NPCLLMChat] ServerManager: Error stopping Piper: {ex.Message}");
                }
            }

            if (whisperProcess != null && !whisperProcess.HasExited)
            {
                try
                {
                    Log.Out("[NPCLLMChat] ServerManager: Stopping Whisper STT...");
                    whisperProcess.Kill();
                    whisperProcess.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning($"[NPCLLMChat] ServerManager: Error stopping Whisper: {ex.Message}");
                }
            }

            // Note: We intentionally don't stop Ollama as it may be used by other applications
            // and is typically meant to run as a system service

            serversStarted = false;
        }

        private static void KillProcessOnPort(int port, string serverName)
        {
            try
            {
                // Use netstat to find process using the port
                var netstatProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-ano",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                netstatProcess.Start();
                string output = netstatProcess.StandardOutput.ReadToEnd();
                netstatProcess.WaitForExit();

                // Parse netstat output to find PID listening on our port
                string[] lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string line in lines)
                {
                    if (line.Contains($":{port}") && line.Contains("LISTENING"))
                    {
                        string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0)
                        {
                            string pidStr = parts[parts.Length - 1];
                            if (int.TryParse(pidStr, out int pid) && pid > 0)
                            {
                                try
                                {
                                    Process existingProcess = Process.GetProcessById(pid);
                                    Log.Out($"[NPCLLMChat] ServerManager: Killing existing {serverName} server (PID: {pid})");
                                    existingProcess.Kill();
                                    existingProcess.WaitForExit(2000); // Wait up to 2 seconds
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning($"[NPCLLMChat] ServerManager: Could not kill process {pid}: {ex.Message}");
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] ServerManager: Error checking port {port}: {ex.Message}");
            }
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
