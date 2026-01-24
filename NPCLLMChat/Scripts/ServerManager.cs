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

            // Check if Ollama is running (don't auto-start - causes Steam hang issues)
            CheckOllamaStatus();

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
            Log.Warning("[NPCLLMChat] ServerManager: Run 'ollama serve' or enable Ollama auto-start in Windows.");
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
        
        private static void KillProcessTree(int pid)
        {
            try
            {
                // Use taskkill to forcefully terminate the process and all child processes
                var killProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c taskkill /F /T /PID {pid}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                killProcess.Start();
                // Don't wait - let taskkill run async to avoid blocking game exit
            }
            catch (Exception ex)
            {
                Log.Warning($"[NPCLLMChat] ServerManager: taskkill failed for PID {pid}: {ex.Message}");
            }
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

        private static bool FindPythonEnvironment(string serverDir, out string pythonExe, out string sitePackages)
        {
            pythonExe = null;
            sitePackages = null;

            // Look for bundled site-packages (portable approach)
            string bundledSitePackages = Path.Combine(serverDir, "venv", "Lib", "site-packages");
            if (Directory.Exists(bundledSitePackages))
            {
                sitePackages = bundledSitePackages;
                Log.Out($"[NPCLLMChat] ServerManager: Found bundled packages at {sitePackages}");
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
