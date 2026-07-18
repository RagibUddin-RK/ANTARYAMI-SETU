using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;

namespace AntaryamiSetuAdmin
{
    public class DevicesForm : Form
    {
        private FlowLayoutPanel _nodesPanel;
        private Label _lblStats;
        private Image? _bgImage;
        private System.Windows.Forms.Timer _refreshTimer;

        private readonly Color ColBg     = Color.FromArgb(5, 5, 5);
        private readonly Color ColGray   = Color.FromArgb(120, 120, 120);
        private readonly Color ColGreen  = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan   = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed    = Color.FromArgb(220, 30, 30);

        // Keep track of cards to prevent blinking (Controls.Clear causes the flicker)
        private Dictionary<string, Panel> _activeCards = new Dictionary<string, Panel>();

        public DevicesForm()
        {
            InitializeGUI();
            UpdateNodeUI();

            // Refresh UI list every 2 seconds automatically without blinking
            _refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _refreshTimer.Tick += (s, e) => UpdateNodeUI();
            _refreshTimer.Start();
        }

        private void InitializeGUI()
        {
            this.Text = "ANTARYAMI - CONNECTED DEVICES DIRECTORY";
            this.Size = new Size(1100, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 9.5F);
            this.DoubleBuffered = true;

            // Load background image if available
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("AntaryamiSetuAdmin.backround_bg.png") ?? 
                                    assembly.GetManifestResourceStream("AntaryamiSetuAdmin.backround_bg.jpg"))
                {
                    if (stream != null) _bgImage = Image.FromStream(stream);
                }
            }
            catch { }

            if (_bgImage != null)
            {
                this.BackgroundImage = _bgImage;
                this.BackgroundImageLayout = ImageLayout.Stretch;
            }

            // ── FOOLPROOF GRID LAYOUT (TableLayoutPanel) ──
            // Using a TableLayoutPanel physically partitions the screen coordinates, 
            // completely preventing Z-order docking overlaps (which cut cards in half).
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));   // Row 0: Top Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Row 1: Connected Cards

            // Top Header Panel (Now fills Row 0 cleanly)
            Panel pnlHeader = new Panel 
            { 
                Dock = DockStyle.Fill, 
                BackColor = Color.FromArgb(240, 10, 15, 20),
                Padding = new Padding(10)
            };
            
            Label lblTitle = new Label 
            { 
                Text = "📡 CONNECTED NODE NETWORK DIRECTORY", 
                Font = new Font("Consolas", 15F, FontStyle.Bold), 
                ForeColor = ColGreen, 
                Dock = DockStyle.Left, 
                TextAlign = ContentAlignment.MiddleLeft, 
                Padding = new Padding(15, 0, 0, 0),
                AutoSize = true 
            };
            
            _lblStats = new Label 
            { 
                Text = "NODES: 0  |  ACTIVE: 0  |  SIGNAL: ENCRYPTED", 
                Font = new Font("Consolas", 10F, FontStyle.Bold), 
                ForeColor = Color.DarkSeaGreen, 
                Dock = DockStyle.Right, 
                TextAlign = ContentAlignment.MiddleRight, 
                Padding = new Padding(0, 0, 15, 0),
                AutoSize = true 
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(_lblStats);

            // FlowLayout Nodes container (Now fills Row 1 cleanly with padding)
            _nodesPanel = new FlowLayoutPanel 
            { 
                Dock = DockStyle.Fill, 
                AutoScroll = true, 
                BackColor = Color.FromArgb(160, 5, 10, 15), 
                Padding = new Padding(20) 
            };
            
            // Enable double buffering on the flow layout panel dynamically
            var doubleBufferProp = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (doubleBufferProp != null)
            {
                doubleBufferProp.SetValue(_nodesPanel, true, null);
            }

            // Add elements into designated table cells
            mainLayout.Controls.Add(pnlHeader, 0, 0);
            mainLayout.Controls.Add(_nodesPanel, 0, 1);
            
            this.Controls.Add(mainLayout);
        }

        private void UpdateNodeUI()
        {
            if (InvokeRequired) { Invoke(new Action(UpdateNodeUI)); return; }

            int total = DashboardForm.KnownDevices.Count;
            int online = 0;
            int offline = 0;
            foreach (var device in DashboardForm.KnownDevices.Values)
            {
                if (DashboardForm.IsDeviceOnline(device.DeviceId, out _))
                    online++;
                else
                    offline++;
            }
            _lblStats.Text = $"NODES: {total}  |  ACTIVE: {online}  |  OFFLINE: {offline}  |  SIGNAL: ENCRYPTED";

            // List of keys currently known
            HashSet<string> currentKeys = new HashSet<string>(DashboardForm.KnownDevices.Keys);

            // 1. Remove devices no longer in known history
            List<string> toRemove = new List<string>();
            foreach (var key in _activeCards.Keys)
            {
                if (!currentKeys.Contains(key))
                {
                    toRemove.Add(key);
                }
            }
            foreach (var key in toRemove)
            {
                _nodesPanel.Controls.Remove(_activeCards[key]);
                _activeCards[key].Dispose();
                _activeCards.Remove(key);
            }

            // 2. Add or Update devices
            foreach (var kvp in DashboardForm.KnownDevices)
            {
                string deviceId = kvp.Key;
                var device = kvp.Value;
                bool isOnline = DashboardForm.IsDeviceOnline(deviceId, out var session);

                if (!_activeCards.ContainsKey(deviceId))
                {
                    // Create new modern card panel (Height reduced to 180 to fit everything cleanly)
                    Panel pnlCard = new Panel 
                    { 
                        Width = 320, 
                        Height = 180, 
                        BackColor = Color.FromArgb(40, 20, 30, 40), 
                        Margin = new Padding(15), 
                        BorderStyle = BorderStyle.None,
                        Name = deviceId
                    };
                    pnlCard.Paint += (s, e) => {
                        // Draw sci-fi glassmorphic border with custom corners
                        Graphics g = e.Graphics;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        bool currentOnline = DashboardForm.IsDeviceOnline(deviceId, out _);
                        Color borderCol = currentOnline ? (device.IsHttp ? ColCyan : ColGreen) : ColGray;
                        using (Pen cardPen = new Pen(Color.FromArgb(100, borderCol), 1))
                        {
                            g.DrawRectangle(cardPen, 0, 0, pnlCard.Width - 1, pnlCard.Height - 1);
                        }
                    };

                    Label lblId = new Label 
                    { 
                        Text = $"UID: {device.DeviceId}", 
                        ForeColor = ColGray, 
                        Font = new Font("Consolas", 8F), 
                        Location = new Point(15, 12), 
                        Width = 290,
                        Height = 25,
                        BackColor = Color.Transparent 
                    };
                    lblId.Name = "lblId";

                    Label lblOs = new Label 
                    { 
                        Text = $"OS: {device.OSInfo}", 
                        ForeColor = ColGray, 
                        Font = new Font("Consolas", 8F), 
                        Location = new Point(15, 38), 
                        Width = 290,
                        Height = 25,
                        BackColor = Color.Transparent 
                    };
                    lblOs.Name = "lblOs";

                    Label lblName = new Label 
                    { 
                        Text = $"> {device.DeviceName}", 
                        ForeColor = Color.White, 
                        Font = new Font("Consolas", 12F, FontStyle.Bold), 
                        Location = new Point(15, 62), 
                        Width = 290,
                        Height = 25,
                        BackColor = Color.Transparent 
                    };
                    lblName.Name = "lblName";
                    
                    string statusText = isOnline 
                        ? (device.IsHttp ? "● STATUS: ONLINE (HTTP WEB)" : "● STATUS: ONLINE (LAN TCP)") 
                        : "● STATUS: OFFLINE";
                    Color statusColor = isOnline ? (device.IsHttp ? ColCyan : ColGreen) : ColRed;
                    Label lblStat = new Label 
                    { 
                        Text = statusText, 
                        ForeColor = statusColor, 
                        Font = new Font("Consolas", 9F, FontStyle.Bold), 
                        Location = new Point(15, 90), 
                        Width = 290,
                        Height = 22,
                        BackColor = Color.Transparent 
                    };
                    lblStat.Name = "lblStat";

                    Button btnCmd = new Button 
                    { 
                        Name = "btnCmd",
                        Text = isOnline ? "CONT.." : "VIEW DATA", 
                        Width = 130, 
                        Height = 32, 
                        Location = new Point(15, 128), 
                        BackColor = Color.Transparent, 
                        ForeColor = ColCyan, 
                        Enabled = true,
                        FlatStyle = FlatStyle.Flat, 
                        Font = new Font("Consolas", 9F, FontStyle.Bold), 
                        Cursor = Cursors.Hand
                    };
                    btnCmd.FlatAppearance.BorderColor = ColCyan;
                    btnCmd.FlatAppearance.BorderSize = 1;
                    btnCmd.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, ColCyan);
                    btnCmd.Click += (sender, e) => 
                    { 
                        if (DashboardForm.IsDeviceOnline(deviceId, out var activeSession) && activeSession != null)
                        {
                            TerminalForm term = new TerminalForm(activeSession); 
                            term.Icon = this.Icon; 
                            term.Show(); 
                        }
                        else
                        {
                            ClientSession dummySession = new ClientSession 
                            { 
                                DeviceId = device.DeviceId, 
                                DeviceName = device.DeviceName, 
                                OSInfo = device.OSInfo,
                                IsActive = false
                            };
                            TerminalForm tForm = new TerminalForm(dummySession);
                            tForm.Icon = this.Icon;
                            tForm.Show();
                        }
                    };

                    Button btnKill = new Button 
                    { 
                        Name = "btnKill",
                        Text = "KILL", 
                        Width = 130, 
                        Height = 32, 
                        Location = new Point(170, 128), 
                        BackColor = Color.Transparent, 
                        ForeColor = isOnline ? ColRed : ColGray, 
                        Enabled = isOnline,
                        FlatStyle = FlatStyle.Flat, 
                        Font = new Font("Consolas", 9F, FontStyle.Bold), 
                        Cursor = isOnline ? Cursors.Hand : Cursors.Default
                    };
                    btnKill.FlatAppearance.BorderColor = isOnline ? ColRed : ColGray;
                    btnKill.FlatAppearance.BorderSize = 1;
                    btnKill.FlatAppearance.MouseOverBackColor = isOnline ? Color.FromArgb(40, ColRed) : Color.Transparent;
                    btnKill.Click += (sender, e) => 
                    {
                        if (DashboardForm.IsDeviceOnline(deviceId, out var activeSession) && activeSession != null)
                        {
                            if (activeSession.IsHttp)
                            {
                                DashboardForm.SendHttpCommand(activeSession.DeviceId ?? "unknown", "cmd /c taskkill /F /IM AntaryamiSetuOnlineAgent.exe");
                            }
                            else
                            {
                                try 
                                {
                                    byte[] cmd = System.Text.Encoding.UTF8.GetBytes("cmd /c taskkill /F /IM AntaryamiSetuAgent.exe");
                                    byte[] pkt = new byte[5 + cmd.Length];
                                    pkt[0] = 2;
                                    Array.Copy(BitConverter.GetBytes(cmd.Length), 0, pkt, 1, 4);
                                    Array.Copy(cmd, 0, pkt, 5, cmd.Length);
                                    activeSession.Stream!.Write(pkt, 0, pkt.Length);
                                    activeSession.Stream.Flush();
                                } 
                                catch {}
                            }
                        }
                    };

                    pnlCard.Controls.Add(lblId); 
                    pnlCard.Controls.Add(lblOs); 
                    pnlCard.Controls.Add(lblName);
                    pnlCard.Controls.Add(lblStat); 
                    pnlCard.Controls.Add(btnCmd); 
                    pnlCard.Controls.Add(btnKill);
                    
                    _nodesPanel.Controls.Add(pnlCard);
                    _activeCards[deviceId] = pnlCard;
                }
                else
                {
                    // Card already exists, just update labels to prevent redrawing/flickering
                    Panel pnlCard = _activeCards[deviceId];
                    Control[] idCtrls = pnlCard.Controls.Find("lblId", true);
                    Control[] osCtrls = pnlCard.Controls.Find("lblOs", true);
                    Control[] nameCtrls = pnlCard.Controls.Find("lblName", true);
                    Control[] statCtrls = pnlCard.Controls.Find("lblStat", true);
                    Control[] cmdCtrls = pnlCard.Controls.Find("btnCmd", true);
                    Control[] killCtrls = pnlCard.Controls.Find("btnKill", true);

                    if (idCtrls.Length > 0) idCtrls[0].Text = $"UID: {device.DeviceId}";
                    if (osCtrls.Length > 0) osCtrls[0].Text = $"OS: {device.OSInfo}";
                    if (nameCtrls.Length > 0) nameCtrls[0].Text = $"> {device.DeviceName}";
                    
                    if (statCtrls.Length > 0)
                    {
                        statCtrls[0].Text = isOnline 
                            ? (device.IsHttp ? "● STATUS: ONLINE (HTTP WEB)" : "● STATUS: ONLINE (LAN TCP)") 
                            : "● STATUS: OFFLINE";
                        statCtrls[0].ForeColor = isOnline ? (device.IsHttp ? ColCyan : ColGreen) : ColRed;
                    }

                    if (cmdCtrls.Length > 0 && cmdCtrls[0] is Button btnCmd)
                    {
                        btnCmd.Text = isOnline ? "CONT.." : "VIEW DATA";
                        btnCmd.Enabled = true;
                        btnCmd.ForeColor = ColCyan;
                        btnCmd.FlatAppearance.BorderColor = ColCyan;
                        btnCmd.Cursor = Cursors.Hand;
                    }

                    if (killCtrls.Length > 0 && killCtrls[0] is Button btnKill)
                    {
                        btnKill.Enabled = isOnline;
                        btnKill.ForeColor = isOnline ? ColRed : ColGray;
                        btnKill.FlatAppearance.BorderColor = isOnline ? ColRed : ColGray;
                        btnKill.Cursor = isOnline ? Cursors.Hand : Cursors.Default;
                    }

                    pnlCard.Invalidate();
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            base.OnFormClosing(e);
        }
    }
}
