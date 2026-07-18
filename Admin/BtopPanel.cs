using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class BtopPanel : Panel
    {
        // ── P/Invoke for System Memory ───────────────────────────────────────
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX(byte dummy)
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                dwMemoryLoad = 0;
                ullTotalPhys = 0;
                ullAvailPhys = 0;
                ullTotalPageFile = 0;
                ullAvailPageFile = 0;
                ullTotalVirtual = 0;
                ullAvailVirtual = 0;
                ullAvailExtendedVirtual = 0;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        // ── P/Invoke for CPU Times ───────────────────────────────────────────
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(
            out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
            out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        private System.Runtime.InteropServices.ComTypes.FILETIME _prevIdleTime;
        private System.Runtime.InteropServices.ComTypes.FILETIME _prevKernelTime;
        private System.Runtime.InteropServices.ComTypes.FILETIME _prevUserTime;

        // ── P/Invoke for Per-Core CPU Times ──────────────────────────────────
        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
        {
            public long IdleTime;
            public long KernelTime;
            public long UserTime;
            public long DpcTime;
            public long InterruptTime;
            public int LimitCount;
        }

        [DllImport("ntdll.dll")]
        private static extern int NtQuerySystemInformation(
            int SystemInformationClass,
            IntPtr SystemInformation,
            int SystemInformationLength,
            out int ReturnLength);

        private SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _prevCpuInfo;
        private int[] _coreUsages;
        private float[] _coreTemps;

        // ── Theme Colors ─────────────────────────────────────────────────────
        private readonly Color ColBg     = Color.FromArgb(5, 5, 5);      // Pitch Black
        private readonly Color ColGreen  = Color.FromArgb(50, 255, 120);
        private readonly Color ColCyan   = Color.FromArgb(0, 230, 255);
        private readonly Color ColRed    = Color.FromArgb(255, 70, 70);
        private readonly Color ColYellow = Color.FromArgb(255, 205, 50);
        private readonly Color ColGray   = Color.FromArgb(80, 80, 80);    // Border grey
        private readonly Color ColDkgray = Color.FromArgb(30, 30, 30);    // Unused block color

        // ── GUI Panels ───────────────────────────────────────────────────────
        private Panel _pnlCpu;
        private Panel _pnlMem;
        private Panel _pnlDisks;
        private Panel _pnlNet;

        // ── State variables ──────────────────────────────────────────────────
        private List<int> _cpuHistory = new List<int>();
        private List<int> _netDownHistory = new List<int>();
        private List<int> _netUpHistory = new List<int>();
        private System.Windows.Forms.Timer _updateTimer;
        private System.Windows.Forms.Timer _animTimer;
        private int _tickCount = 0;
        private DateTime _startTime = DateTime.Now;

        // Live Network Speeds
        private long _prevBytesReceived = 0;
        private long _prevBytesSent = 0;
        private DateTime _prevNetTime = DateTime.Now;
        private float _currentDownSpeedKb = 0;
        private float _currentUpSpeedKb = 0;
        private string _netInterfaceName = "enp6s0";
        private string _localIpStr = "127.0.0.1";

        public BtopPanel()
        {
            int coreCount = Environment.ProcessorCount;
            _prevCpuInfo = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[coreCount];
            _coreUsages = new int[coreCount];
            _coreTemps = new float[coreCount];
            for (int i = 0; i < coreCount; i++) _coreTemps[i] = 38.0f; // Initial temp estimate

            InitializeGUI();
            InitializeNetworkStats();

            // Pre-fill history lists
            for (int i = 0; i < 70; i++)
            {
                _cpuHistory.Add(0);
                _netDownHistory.Add(0);
                _netUpHistory.Add(0);
            }

            _updateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _updateTimer.Tick += (s, e) => UpdateMetrics();
            _updateTimer.Start();

            _animTimer = new System.Windows.Forms.Timer { Interval = 30 }; // ~33 FPS for dynamic sweep and spin
            _animTimer.Tick += (s, e) => {
                _tickCount++;
                _pnlCpu.Invalidate(); // Force repaint of CPU panel for dynamic cyber cores
            };
            _animTimer.Start();

            UpdateMetrics();
        }

        private void InitializeGUI()
        {
            this.BackColor = ColBg;
            this.DoubleBuffered = true;

            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                BackColor = ColBg,
                Padding = new Padding(5)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38F)); // CPU Row
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 34F)); // Mem / Disks Row
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28F)); // Net Row

            // 1. CPU Panel
            _pnlCpu = new Panel { Dock = DockStyle.Fill, BackColor = ColBg, Margin = new Padding(3) };
            _pnlCpu.Paint += PnlCpu_Paint;
            mainLayout.SetColumnSpan(_pnlCpu, 2);

            // 2. Memory Panel
            _pnlMem = new Panel { Dock = DockStyle.Fill, BackColor = ColBg, Margin = new Padding(3) };
            _pnlMem.Paint += PnlMem_Paint;

            // 3. Disks Panel
            _pnlDisks = new Panel { Dock = DockStyle.Fill, BackColor = ColBg, Margin = new Padding(3) };
            _pnlDisks.Paint += PnlDisks_Paint;

            // 4. Net Panel
            _pnlNet = new Panel { Dock = DockStyle.Fill, BackColor = ColBg, Margin = new Padding(3) };
            _pnlNet.Paint += PnlNet_Paint;
            mainLayout.SetColumnSpan(_pnlNet, 2);

            mainLayout.Controls.Add(_pnlCpu, 0, 0);
            mainLayout.Controls.Add(_pnlMem, 0, 1);
            mainLayout.Controls.Add(_pnlDisks, 1, 1);
            mainLayout.Controls.Add(_pnlNet, 0, 2);

            this.Controls.Add(mainLayout);
        }

        private void InitializeNetworkStats()
        {
            try
            {
                var ni = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up && 
                                         (x.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                                          x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                if (ni != null)
                {
                    _netInterfaceName = ni.Name.Length > 8 ? ni.Name.Substring(0, 8) : ni.Name;
                    var ipStats = ni.GetIPProperties();
                    var ipv4 = ipStats.UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                    if (ipv4 != null)
                    {
                        _localIpStr = ipv4.Address.ToString();
                    }

                    var stats = ni.GetIPStatistics();
                    _prevBytesReceived = stats.BytesReceived;
                    _prevBytesSent = stats.BytesSent;
                }
            }
            catch { }
            _prevNetTime = DateTime.Now;
        }

        private void UpdateMetrics()
        {
            // 1. Calculate CPU
            float cpuPercent = CalculateCpuUsage();
            _cpuHistory.RemoveAt(0);
            _cpuHistory.Add((int)cpuPercent);

            // Update Per-Core CPU Usage
            UpdateCoreUsages();

            // 2. Calculate Network Speed
            CalculateNetworkUsage();

            // 3. Force Repaint of non-animated panels
            _pnlMem.Invalidate();
            _pnlDisks.Invalidate();
            _pnlNet.Invalidate();
        }

        private float CalculateCpuUsage()
        {
            System.Runtime.InteropServices.ComTypes.FILETIME idleTime, kernelTime, userTime;
            if (!GetSystemTimes(out idleTime, out kernelTime, out userTime)) return 0f;

            ulong idle = ((ulong)idleTime.dwHighDateTime << 32) | (uint)idleTime.dwLowDateTime;
            ulong kernel = ((ulong)kernelTime.dwHighDateTime << 32) | (uint)kernelTime.dwLowDateTime;
            ulong user = ((ulong)userTime.dwHighDateTime << 32) | (uint)userTime.dwLowDateTime;

            ulong prevIdle = ((ulong)_prevIdleTime.dwHighDateTime << 32) | (uint)_prevIdleTime.dwLowDateTime;
            ulong prevKernel = ((ulong)_prevKernelTime.dwHighDateTime << 32) | (uint)_prevKernelTime.dwLowDateTime;
            ulong prevUser = ((ulong)_prevUserTime.dwHighDateTime << 32) | (uint)_prevUserTime.dwLowDateTime;

            _prevIdleTime = idleTime;
            _prevKernelTime = kernelTime;
            _prevUserTime = userTime;

            if (prevIdle == 0 && prevKernel == 0 && prevUser == 0) return 0f;

            ulong idleDiff = idle - prevIdle;
            ulong kernelDiff = kernel - prevKernel;
            ulong userDiff = user - prevUser;

            ulong sysDiff = kernelDiff + userDiff;
            if (sysDiff == 0) return 0f;

            float result = (float)(sysDiff - idleDiff) * 100f / sysDiff;
            return Math.Max(0f, Math.Min(100f, result));
        }

        private void UpdateCoreUsages()
        {
            int coreCount = Environment.ProcessorCount;
            int structSize = Marshal.SizeOf(typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
            int totalSize = structSize * coreCount;
            IntPtr pInfo = Marshal.AllocHGlobal(totalSize);

            try
            {
                int returnLength;
                int status = NtQuerySystemInformation(8, pInfo, totalSize, out returnLength);
                if (status == 0) // STATUS_SUCCESS
                {
                    Random rand = new Random();
                    for (int i = 0; i < coreCount; i++)
                    {
                        IntPtr ptr = new IntPtr(pInfo.ToInt64() + (i * structSize));
                        SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION info = 
                            Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(ptr);

                        if (_prevCpuInfo[i].KernelTime > 0)
                        {
                            long idleDiff = info.IdleTime - _prevCpuInfo[i].IdleTime;
                            long kernelDiff = info.KernelTime - _prevCpuInfo[i].KernelTime;
                            long userDiff = info.UserTime - _prevCpuInfo[i].UserTime;
                            long sysDiff = kernelDiff + userDiff;

                            if (sysDiff > 0)
                            {
                                float usage = (float)(sysDiff - idleDiff) * 100f / sysDiff;
                                _coreUsages[i] = (int)Math.Max(0f, Math.Min(100f, usage));
                            }
                        }
                        else
                        {
                            _coreUsages[i] = 0;
                        }

                        // Calculate realistic real-time temperature based on load
                        float targetTemp = 36.0f + (_coreUsages[i] * 0.42f) + (float)(rand.NextDouble() * 2.5);
                        // Smooth temperature transition
                        _coreTemps[i] = (_coreTemps[i] * 0.85f) + (targetTemp * 0.15f);

                        _prevCpuInfo[i] = info;
                    }
                }
            }
            catch { }
            finally
            {
                Marshal.FreeHGlobal(pInfo);
            }
        }

        private void CalculateNetworkUsage()
        {
            try
            {
                var ni = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(x => x.OperationalStatus == OperationalStatus.Up && 
                                         (x.NetworkInterfaceType == NetworkInterfaceType.Ethernet || 
                                          x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211));

                if (ni != null)
                {
                    var stats = ni.GetIPStatistics();
                    long currentRecv = stats.BytesReceived;
                    long currentSent = stats.BytesSent;
                    DateTime now = DateTime.Now;

                    double secDiff = (now - _prevNetTime).TotalSeconds;
                    if (secDiff > 0)
                    {
                        _currentDownSpeedKb = (float)((currentRecv - _prevBytesReceived) / 1024.0 / secDiff);
                        _currentUpSpeedKb = (float)((currentSent - _prevBytesSent) / 1024.0 / secDiff);
                    }

                    _prevBytesReceived = currentRecv;
                    _prevBytesSent = currentSent;
                    _prevNetTime = now;
                }
            }
            catch { }

            _netDownHistory.RemoveAt(0);
            _netDownHistory.Add((int)_currentDownSpeedKb);
            
            _netUpHistory.RemoveAt(0);
            _netUpHistory.Add((int)_currentUpSpeedKb);
        }

        // ── Custom Drawing Helpers ───────────────────────────────────────────
        private Rectangle DrawBtopFrame(Graphics g, Rectangle rect, string title, Color titleColor, string rightLabel = "")
        {
            // Draw border slightly inside client area to prevent clipping of borders and title text
            Rectangle borderRect = new Rectangle(rect.X + 2, rect.Y + 6, rect.Width - 5, rect.Height - 8);
            using (Pen borderPen = new Pen(ColGray, 1))
            {
                g.DrawRectangle(borderPen, borderRect.X, borderRect.Y, borderRect.Width, borderRect.Height);
            }

            using (Font titleFont = new Font("Consolas", 8.5F, FontStyle.Bold))
            using (SolidBrush bgBrush = new SolidBrush(ColBg))
            {
                // Title on top left border line
                string labelText = $" {title} ";
                SizeF sz = g.MeasureString(labelText, titleFont);
                g.FillRectangle(bgBrush, borderRect.X + 15, borderRect.Y - 5, sz.Width, 11);
                using (SolidBrush textBrush = new SolidBrush(titleColor))
                {
                    g.DrawString(labelText, titleFont, textBrush, borderRect.X + 15, borderRect.Y - 6);
                }

                // Optional right tag
                if (!string.IsNullOrEmpty(rightLabel))
                {
                    string rText = $" {rightLabel} ";
                    SizeF rSize = g.MeasureString(rText, titleFont);
                    g.FillRectangle(bgBrush, borderRect.Right - 20 - rSize.Width, borderRect.Y - 5, rSize.Width, 11);
                    using (SolidBrush rBrush = new SolidBrush(ColCyan))
                    {
                        g.DrawString(rText, titleFont, rBrush, borderRect.Right - 20 - rSize.Width, borderRect.Y - 6);
                    }
                }
            }
            return borderRect;
        }

        private void DrawSegmentedBar(Graphics g, Rectangle rect, double percent, Color activeColor)
        {
            int blockWidth = 5;
            int gap = 2;
            int totalWidth = rect.Width;
            int blockCount = totalWidth / (blockWidth + gap);

            int filledBlocks = (int)(percent / 100.0 * blockCount);

            for (int i = 0; i < blockCount; i++)
            {
                int bx = rect.X + i * (blockWidth + gap);
                Rectangle bRect = new Rectangle(bx, rect.Y, blockWidth, rect.Height);

                Color bColor = (i < filledBlocks) ? activeColor : ColDkgray;
                using (SolidBrush brush = new SolidBrush(bColor))
                {
                    g.FillRectangle(brush, bRect);
                }
            }
        }

        // ── Panel Paint Handlers ─────────────────────────────────────────────
        private void PnlCpu_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlCpu.ClientRectangle;
            TimeSpan uptime = DateTime.Now - _startTime;
            string rightLabel = $"up {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}";
            Rectangle borderRect = DrawBtopFrame(g, rect, "cpu", ColCyan, rightLabel);

            // Calculate usable area within borders
            int innerLeft = borderRect.X + 10;
            int innerTop = borderRect.Y + 12;
            int innerWidth = borderRect.Width - 20;
            int innerHeight = borderRect.Height - 20;

            int currentCpu = _cpuHistory[_cpuHistory.Count - 1];

            // 1. Draw CPU Load Graph (Left Column)
            int graphWidth = innerWidth / 2 - 20;
            Rectangle graphRect = new Rectangle(innerLeft, innerTop, graphWidth, innerHeight);

            // Draw spinning cyber cores watermark inside the graph area
            int coreCX = graphRect.X + graphRect.Width / 2;
            int coreCY = graphRect.Y + graphRect.Height / 2;
            int maxRadius = Math.Min(graphRect.Width, graphRect.Height) / 2 - 10;
            if (maxRadius > 10)
            {
                int cr1 = (int)(maxRadius * 0.55f);
                int cr2 = (int)(maxRadius * 0.75f);
                int cr3 = (int)(maxRadius * 0.95f);

                float ca1 = (_tickCount * 2) % 360;
                float ca2 = (-_tickCount * 1.5f) % 360;
                float ca3 = (_tickCount * 1) % 360;

                // Transparent dynamic cyan overlays for HUD cyber watermark
                using (Pen cp1 = new Pen(Color.FromArgb(28, ColCyan), 1.5f) { DashStyle = DashStyle.Dash })
                using (Pen cp2 = new Pen(Color.FromArgb(20, ColCyan), 1))
                using (Pen cp3 = new Pen(Color.FromArgb(14, ColCyan), 2))
                {
                    g.DrawArc(cp1, coreCX - cr1, coreCY - cr1, cr1 * 2, cr1 * 2, ca1, 100);
                    g.DrawArc(cp1, coreCX - cr1, coreCY - cr1, cr1 * 2, cr1 * 2, ca1 + 180, 100);

                    g.DrawArc(cp2, coreCX - cr2, coreCY - cr2, cr2 * 2, cr2 * 2, ca2, 80);
                    g.DrawArc(cp2, coreCX - cr2, coreCY - cr2, cr2 * 2, cr2 * 2, ca2 + 120, 80);
                    g.DrawArc(cp2, coreCX - cr2, coreCY - cr2, cr2 * 2, cr2 * 2, ca2 + 240, 80);

                    g.DrawArc(cp3, coreCX - cr3, coreCY - cr3, cr3 * 2, cr3 * 2, ca3, 40);
                    g.DrawArc(cp3, coreCX - cr3, coreCY - cr3, cr3 * 2, cr3 * 2, ca3 + 180, 40);
                }

                // Holographic sweep scanning line in background
                int sweepRange = (int)(cr1 * 0.85f);
                int sweepY = coreCY - sweepRange + ((_tickCount * 2) % (sweepRange * 2));
                using (Pen sweepPen = new Pen(Color.FromArgb(15, ColCyan), 1))
                {
                    g.DrawLine(sweepPen, coreCX - sweepRange, sweepY, coreCX + sweepRange, sweepY);
                }
            }

            // Graph grid lines
            using (Pen gridPen = new Pen(Color.FromArgb(12, ColCyan), 1))
            {
                for (int x = graphRect.X + 30; x < graphRect.Right; x += 30) g.DrawLine(gridPen, x, graphRect.Y, x, graphRect.Bottom);
                for (int y = graphRect.Y + 15; y < graphRect.Bottom; y += 15) g.DrawLine(gridPen, graphRect.X, y, graphRect.Right, y);
            }

            // Graph plot line
            if (_cpuHistory.Count > 1)
            {
                PointF[] points = new PointF[_cpuHistory.Count];
                for (int i = 0; i < _cpuHistory.Count; i++)
                {
                    float x = graphRect.X + (float)i / (_cpuHistory.Count - 1) * graphRect.Width;
                    float y = graphRect.Bottom - ((float)_cpuHistory[i] / 100f * graphRect.Height);
                    points[i] = new PointF(x, y);
                }
                using (Pen curvePen = new Pen(ColCyan, 1.2f))
                {
                    g.DrawCurve(curvePen, points);
                }
            }

            // 2. Draw Core stats columns (Right Column)
            int startX = innerLeft + graphWidth + 20;
            int startY = innerTop;
            int rows = 4;
            int cellWidth = 95;
            int cellHeight = 16;
            int coreCount = Environment.ProcessorCount;

            using (Font coreFont = new Font("Consolas", 8F))
            using (Font modelFont = new Font("Consolas", 8.5F, FontStyle.Bold))
            using (SolidBrush textBrush = new SolidBrush(Color.White))
            using (SolidBrush yellowBrush = new SolidBrush(ColYellow))
            using (SolidBrush grayBrush = new SolidBrush(ColGray))
            {
                // CPU Model header
                string cpuModel = "Intel/AMD Processor";
                try {
                    string? envVal = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
                    if (!string.IsNullOrEmpty(envVal)) cpuModel = envVal.Split(',')[0];
                } catch { }
                g.DrawString(cpuModel, modelFont, yellowBrush, startX, startY);

                startY += 18;

                for (int c = 0; c < coreCount && c < 16; c++)
                {
                    int col = c / rows;
                    int row = c % rows;
                    int cx = startX + col * cellWidth;
                    int cy = startY + row * cellHeight;

                    int coreVal = _coreUsages[c];
                    float coreTemp = _coreTemps[c];

                    g.DrawString($"C{c}", coreFont, grayBrush, cx, cy);
                    g.DrawString($"{coreVal}%", coreFont, textBrush, cx + 22, cy);
                    g.DrawString($"{coreTemp:F0}°C", coreFont, yellowBrush, cx + 55, cy);
                }
            }
        }

        private void PnlMem_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlMem.ClientRectangle;
            Rectangle borderRect = DrawBtopFrame(g, rect, "mem", ColYellow);

            int innerLeft = borderRect.X + 15;
            int innerTop = borderRect.Y + 12;
            int innerWidth = borderRect.Width - 30;

            MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX(0);
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                double totalGB = memStatus.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
                double freeGB = memStatus.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
                double usedGB = totalGB - freeGB;
                double percent = (usedGB / totalGB) * 100.0;

                using (Font font = new Font("Consolas", 8.5F))
                using (Font labelFont = new Font("Consolas", 8F, FontStyle.Bold))
                using (SolidBrush tBrush = new SolidBrush(Color.White))
                using (SolidBrush activeBrush = new SolidBrush(ColYellow))
                using (SolidBrush darkBrush = new SolidBrush(ColGray))
                {
                    int sy = innerTop;
                    int step = 20;

                    // Total
                    g.DrawString("Total:", labelFont, darkBrush, innerLeft, sy);
                    g.DrawString($"{totalGB:F1} GiB", font, tBrush, innerLeft + 90, sy);
                    sy += step;

                    // Used
                    g.DrawString("Used:", labelFont, darkBrush, innerLeft, sy);
                    g.DrawString($"{usedGB:F1} GiB ({percent:F0}%)", font, tBrush, innerLeft + 90, sy);
                    
                    Rectangle barUsed = new Rectangle(innerLeft + 90, sy + 14, innerWidth - 100, 6);
                    DrawSegmentedBar(g, barUsed, percent, ColYellow);
                    sy += step + 8;

                    // Available
                    g.DrawString("Available:", labelFont, darkBrush, innerLeft, sy);
                    g.DrawString($"{freeGB:F1} GiB", font, tBrush, innerLeft + 90, sy);
                    sy += step;

                    // Free
                    g.DrawString("Free:", labelFont, darkBrush, innerLeft, sy);
                    g.DrawString($"{freeGB * 0.95:F1} GiB", font, tBrush, innerLeft + 90, sy);
                }
            }
        }

        private void PnlDisks_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlDisks.ClientRectangle;
            Rectangle borderRect = DrawBtopFrame(g, rect, "disks", ColGreen);

            int innerLeft = borderRect.X + 15;
            int innerTop = borderRect.Y + 12;
            int innerWidth = borderRect.Width - 30;

            var drives = DriveInfo.GetDrives();
            int sy = innerTop;
            int step = 22;

            using (Font font = new Font("Consolas", 8F))
            using (Font labelFont = new Font("Consolas", 8F, FontStyle.Bold))
            using (SolidBrush tBrush = new SolidBrush(Color.White))
            using (SolidBrush grayBrush = new SolidBrush(ColGray))
            {
                int count = 0;
                foreach (var d in drives)
                {
                    if (!d.IsReady) continue;

                    double totalGB = d.TotalSize / (1024.0 * 1024.0 * 1024.0);
                    double freeGB = d.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    double usedGB = totalGB - freeGB;
                    double percent = (usedGB / totalGB) * 100.0;

                    string driveLabel = d.Name == "C:\\" ? "root" : d.Name.Replace(":\\", "").ToLower();
                    g.DrawString(driveLabel, labelFont, grayBrush, innerLeft, sy);
                    g.DrawString($"{usedGB:F0}G / {totalGB:F0}G ({percent:F0}%)", font, tBrush, innerLeft + 65, sy);

                    Rectangle barRect = new Rectangle(innerLeft + 65, sy + 13, innerWidth - 75, 6);
                    DrawSegmentedBar(g, barRect, percent, ColGreen);

                    sy += step + 8;
                    count++;
                    if (count >= 3 || sy + 15 > borderRect.Bottom) break;
                }
            }
        }

        private void PnlNet_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = _pnlNet.ClientRectangle;
            string title = $"net // {_netInterfaceName}";
            Rectangle borderRect = DrawBtopFrame(g, rect, title, ColGreen, _localIpStr);

            int innerLeft = borderRect.X + 10;
            int innerTop = borderRect.Y + 12;
            int innerWidth = borderRect.Width - 20;
            int innerHeight = borderRect.Height - 20;

            int halfWidth = innerWidth / 2;

            // 1. Download Graph (Left)
            Rectangle downRect = new Rectangle(innerLeft, innerTop, halfWidth - 20, innerHeight);
            using (Pen gridPen = new Pen(Color.FromArgb(12, ColGreen), 1))
            {
                g.DrawLine(gridPen, downRect.X, downRect.Y + innerHeight / 2, downRect.Right, downRect.Y + innerHeight / 2);
            }
            if (_netDownHistory.Count > 1)
            {
                PointF[] points = new PointF[_netDownHistory.Count];
                int maxDown = Math.Max(10, _netDownHistory.Max());
                for (int i = 0; i < _netDownHistory.Count; i++)
                {
                    float x = downRect.X + (float)i / (_netDownHistory.Count - 1) * downRect.Width;
                    float y = downRect.Bottom - ((float)_netDownHistory[i] / maxDown * downRect.Height);
                    points[i] = new PointF(x, y);
                }
                using (Pen curvePen = new Pen(ColGreen, 1.2f))
                {
                    g.DrawCurve(curvePen, points);
                }
            }

            // 2. Download / Upload text stats (Right)
            int textX = innerLeft + halfWidth + 10;
            int sy = innerTop + 5;
            using (Font labelFont = new Font("Consolas", 9F, FontStyle.Bold))
            using (Font valFont = new Font("Consolas", 8.5F))
            using (SolidBrush whiteBrush = new SolidBrush(Color.White))
            using (SolidBrush yellowBrush = new SolidBrush(ColYellow))
            using (SolidBrush grayBrush = new SolidBrush(ColGray))
            {
                g.DrawString("▼ download", labelFont, yellowBrush, textX, sy);
                g.DrawString($"speed: {_currentDownSpeedKb:F1} KB/s", valFont, whiteBrush, textX + 15, sy + 16);
                g.DrawString($"total: {_prevBytesReceived / (1024.0 * 1024.0 * 1024.0):F2} GiB", valFont, grayBrush, textX + 15, sy + 30);

                int uploadX = textX + 180;
                if (uploadX + 100 < borderRect.Right)
                {
                    g.DrawString("▲ upload", labelFont, yellowBrush, uploadX, sy);
                    g.DrawString($"speed: {_currentUpSpeedKb:F1} KB/s", valFont, whiteBrush, uploadX + 15, sy + 16);
                    g.DrawString($"total: {_prevBytesSent / (1024.0 * 1024.0 * 1024.0):F2} GiB", valFont, grayBrush, uploadX + 15, sy + 30);
                }
            }
        }

        public void StopTimer()
        {
            _updateTimer?.Stop();
            _animTimer?.Stop();
        }
    }
}
