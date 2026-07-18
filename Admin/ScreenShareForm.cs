using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class ScreenShareForm : Form
    {
        private readonly ClientSession _session;
        private PictureBox? _picScreen;
        private Button? _btnToggle;
        private ComboBox? _cbFps;
        private ComboBox? _cbQuality;
        private Label? _lblStatus;
        private bool _isStreaming = false;

        private readonly Color ColBg = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel = Color.FromArgb(12, 12, 12);
        private readonly Color ColGreen = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed = Color.FromArgb(255, 30, 30);
        private readonly Color ColBorder = Color.FromArgb(0, 100, 30);

        public ScreenShareForm(ClientSession session)
        {
            _session = session;
            InitializeGUI();

            // Bind packet listener
            _session.OnScreenFrameReceived = HandleScreenFrame;
            
            // Automatically start streaming on load
            StartStreaming();
        }

        private void InitializeGUI()
        {
            this.Text = $"★ OMNIVISION REMOTE DESKTOP // {_session.DeviceName} ★";
            this.Size = new Size(1000, 680);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 10F);

            // Set Form Icon if available
            this.Icon = Program.GetAppIcon();

            // Main layout Table
            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Controls Row
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Viewport Row
            this.Controls.Add(layout);

            // Controls Panel
            Panel pnlControls = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = ColPanel,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10)
            };
            layout.Controls.Add(pnlControls, 0, 0);

            // START/STOP Button
            _btnToggle = new Button
            {
                Text = "■ STOP STREAM",
                Location = new Point(10, 10),
                Size = new Size(140, 28),
                BackColor = Color.Transparent,
                ForeColor = ColRed,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnToggle.FlatAppearance.BorderColor = ColRed;
            _btnToggle.Click += BtnToggle_Click;
            pnlControls.Controls.Add(_btnToggle);

            // FPS Dropdown
            Label lblFps = new Label { Text = "FPS:", ForeColor = ColCyan, Location = new Point(170, 15), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold) };
            pnlControls.Controls.Add(lblFps);

            _cbFps = new ComboBox
            {
                Location = new Point(210, 11),
                Size = new Size(60, 25),
                BackColor = ColBg,
                ForeColor = ColGreen,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cbFps.Items.AddRange(new object[] { "2", "5", "10", "15", "20" });
            _cbFps.SelectedIndex = 2; // Default 10 FPS
            _cbFps.SelectedIndexChanged += SettingsChanged;
            pnlControls.Controls.Add(_cbFps);

            // Quality Dropdown
            Label lblQuality = new Label { Text = "QUALITY:", ForeColor = ColCyan, Location = new Point(290, 15), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold) };
            pnlControls.Controls.Add(lblQuality);

            _cbQuality = new ComboBox
            {
                Location = new Point(360, 11),
                Size = new Size(70, 25),
                BackColor = ColBg,
                ForeColor = ColGreen,
                FlatStyle = FlatStyle.Flat,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cbQuality.Items.AddRange(new object[] { "20%", "40%", "60%", "80%", "100%" });
            _cbQuality.SelectedIndex = 2; // Default 60%
            _cbQuality.SelectedIndexChanged += SettingsChanged;
            pnlControls.Controls.Add(_cbQuality);

            // Status Label
            _lblStatus = new Label
            {
                Text = "STATUS: ACTIVE STREAM | LATENCY: --",
                ForeColor = Color.Gray,
                Location = new Point(460, 15),
                AutoSize = true,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            };
            pnlControls.Controls.Add(_lblStatus);

            // Back Button
            Button btnClose = new Button
            {
                Text = "◀ CLOSE",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(pnlControls.Width - 100, 10),
                Size = new Size(80, 28),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderColor = Color.White;
            btnClose.Click += (s, e) => this.Close();
            pnlControls.Controls.Add(btnClose);

            // Picture Box Container Panel
            Panel pnlScreen = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };
            layout.Controls.Add(pnlScreen, 0, 1);

            _picScreen = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            pnlScreen.Controls.Add(_picScreen);
        }

        private void BtnToggle_Click(object? sender, EventArgs e)
        {
            if (_isStreaming)
            {
                StopStreaming();
            }
            else
            {
                StartStreaming();
            }
        }

        private void StartStreaming()
        {
            if (!_session.IsActive) return;
            _isStreaming = true;

            if (_btnToggle != null)
            {
                _btnToggle.Text = "■ STOP STREAM";
                _btnToggle.ForeColor = ColRed;
                _btnToggle.FlatAppearance.BorderColor = ColRed;
            }

            if (_lblStatus != null)
            {
                _lblStatus.Text = "STATUS: STARTING STREAM...";
                _lblStatus.ForeColor = ColCyan;
            }

            SendStreamCommand();
        }

        private void StopStreaming()
        {
            _isStreaming = false;

            if (_btnToggle != null)
            {
                _btnToggle.Text = "▶ START STREAM";
                _btnToggle.ForeColor = ColGreen;
                _btnToggle.FlatAppearance.BorderColor = ColGreen;
            }

            if (_lblStatus != null)
            {
                _lblStatus.Text = "STATUS: STREAM PAUSED";
                _lblStatus.ForeColor = Color.Gray;
            }

            SendCommandDirect("screen_stop");
        }

        private void SettingsChanged(object? sender, EventArgs e)
        {
            if (_isStreaming)
            {
                SendStreamCommand();
            }
        }

        private void SendStreamCommand()
        {
            int fps = 10;
            int quality = 60;

            if (_cbFps != null && int.TryParse(_cbFps.Text, out int f)) fps = f;
            if (_cbQuality != null)
            {
                string qText = _cbQuality.Text.Replace("%", "");
                if (int.TryParse(qText, out int q)) quality = q;
            }

            SendCommandDirect($"screen_start {fps} {quality}");
        }

        private void SendCommandDirect(string command)
        {
            if (!_session.IsActive) return;

            if (_session.IsHttp)
            {
                DashboardForm.SendHttpCommand(_session.DeviceId, command);
                return;
            }

            try
            {
                byte[] cmdBytes = Encoding.UTF8.GetBytes(command);
                _session.SendPacket(2, cmdBytes);
            }
            catch { }
        }

        private DateTime _lastFrameTime = DateTime.Now;
        private void HandleScreenFrame(byte[] payload)
        {
            if (InvokeRequired) { Invoke(new Action(() => HandleScreenFrame(payload))); return; }
            if (!_isStreaming) return;

            try
            {
                using MemoryStream ms = new MemoryStream(payload);
                Image img = Image.FromStream(ms);
                
                // Dispose previous image to prevent memory leak
                Image? prev = _picScreen!.Image;
                _picScreen.Image = img;
                prev?.Dispose();

                // Calculate FPS / Frame-receive latency
                var elapsed = DateTime.Now - _lastFrameTime;
                _lastFrameTime = DateTime.Now;
                
                if (_lblStatus != null)
                {
                    _lblStatus.Text = $"STATUS: STREAM ACTIVE | DELTA: {elapsed.TotalMilliseconds:F0}ms | SIZE: {payload.Length / 1024.0:F1} KB";
                    _lblStatus.ForeColor = ColGreen;
                }
            }
            catch
            {
                if (_lblStatus != null)
                {
                    _lblStatus.Text = "STATUS: FRAME ERROR";
                    _lblStatus.ForeColor = ColRed;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopStreaming();
            _session.OnScreenFrameReceived = null;
            base.OnFormClosing(e);
        }
    }
}
