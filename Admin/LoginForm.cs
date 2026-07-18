using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AntaryamiSetuAdmin
{
    public class LoginForm : Form
    {
        public bool IsAuthenticated { get; private set; } = false;

        private enum LoginStep { Username, Password }
        private LoginStep _currentStep = LoginStep.Username;
        
        private TextBox _txtInput;
        private Button _btnLogin;
        
        private Color ColCyan = Color.FromArgb(150, 230, 240); // Matches Plymouth
        private Image? _loginImage;
        private int _tickCount = 0;
        private System.Windows.Forms.Timer _animTimer;

        public LoginForm()
        {
            this.Text = "ANTARYAMI OMNIVISION - LOGIN";
            this.Size = new Size(1280, 720);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized; // Fullscreen on any display
            this.DoubleBuffered = true;
            this.BackColor = Color.FromArgb(10, 15, 20);

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            try {
                using (var stream = assembly.GetManifestResourceStream("AntaryamiSetuAdmin.login_bg.png") ?? assembly.GetManifestResourceStream("AntaryamiSetuAdmin.login_bg.jpg"))
                {
                    if (stream != null) _loginImage = Image.FromStream(stream);
                }
            } catch { }

            _txtInput = CreateGlassTextBox("Enter Username", 515, 450, false);
            
            _btnLogin = new Button
            {
                Text = "NEXT",
                Bounds = new Rectangle(565, 660, 150, 38),
                BackColor = Color.Transparent,
                ForeColor = ColCyan,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Consolas", 12F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnLogin.FlatAppearance.BorderColor = ColCyan;
            _btnLogin.FlatAppearance.BorderSize = 1;
            _btnLogin.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 150, 220, 230);
            _btnLogin.FlatAppearance.MouseDownBackColor = Color.FromArgb(60, 100, 180, 200);
            _btnLogin.Visible = false; // Hidden entirely for auto-advance hacker aesthetic
            _btnLogin.Click += BtnLogin_Click;
            
            this.Controls.Add(_txtInput);
            this.Controls.Add(_btnLogin);
            
            // Keep the invisible textbox focused
            this.Click += (s, e) => _txtInput.Focus();
            this.Shown += (s, e) => _txtInput.Focus();
            
            // Dynamically place underline variables relative to center
            this.Resize += (s, e) => {
                int cx = this.Width / 2;
                int cy = this.Height / 2;
                
                // Hide input offscreen so it's invisible but focusable!
                _txtInput.Location = new Point(-1000, -1000); 
            };
            
            _txtInput.TextChanged += TxtInput_TextChanged;

            this.Paint += LoginForm_Paint;
            
            _animTimer = new System.Windows.Forms.Timer();
            _animTimer.Interval = 30; // 33 FPS infinity loop
            _animTimer.Tick += (s, e) => { _tickCount++; this.Invalidate(); };
            _animTimer.Start();
        }

        private TextBox CreateGlassTextBox(string placeholder, int x, int y, bool isPassword)
        {
            TextBox txt = new TextBox
            {
                Location = new Point(x, y),
                Width = 250,
                BackColor = Color.FromArgb(10, 15, 20),   // Matches the Form's BackColor exactly
                ForeColor = ColCyan,
                Font = new Font("Consolas", 14F),
                BorderStyle = BorderStyle.None,            // no border at all
                Text = placeholder
            };
            txt.GotFocus += (s, e) => { if (txt.Text == placeholder) { txt.Text = ""; } };
            txt.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(txt.Text)) { txt.Text = placeholder; } };
            return txt;
        }

        private void LoginForm_Paint(object? sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Draw background image
            if (_loginImage != null) g.DrawImage(_loginImage, new Rectangle(0, 0, this.Width, this.Height));
            else g.Clear(Color.FromArgb(10, 20, 25));

            // --- DYNAMIC CIRCLES THEME (Spinning Cyber Cores) ---
            int centerX = this.Width / 2;
            int centerY = this.Height / 2; 

            // Calculate dynamic base radius based on display size
            int baseRadius = Math.Min(this.Width, this.Height) / 5;
            int r1 = baseRadius;
            int r2 = (int)(baseRadius * 1.15f);
            int r3 = (int)(baseRadius * 1.3f);

            float a1 = (_tickCount * 5) % 360;
            float a2 = (-_tickCount * 3) % 360;
            float a3 = (_tickCount * 2) % 360;

            using (Pen p1 = new Pen(Color.FromArgb(180, ColCyan), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, DashStyle = DashStyle.Dash })
            using (Pen p2 = new Pen(Color.FromArgb(120, ColCyan), 1) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (Pen p3 = new Pen(Color.FromArgb(80, ColCyan), 3) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(p1, centerX - r1, centerY - r1, r1 * 2, r1 * 2, a1, 100);
                g.DrawArc(p1, centerX - r1, centerY - r1, r1 * 2, r1 * 2, a1 + 180, 100);

                g.DrawArc(p2, centerX - r2, centerY - r2, r2 * 2, r2 * 2, a2, 80);
                g.DrawArc(p2, centerX - r2, centerY - r2, r2 * 2, r2 * 2, a2 + 120, 80);
                g.DrawArc(p2, centerX - r2, centerY - r2, r2 * 2, r2 * 2, a2 + 240, 80);

                g.DrawArc(p3, centerX - r3, centerY - r3, r3 * 2, r3 * 2, a3, 40);
                g.DrawArc(p3, centerX - r3, centerY - r3, r3 * 2, r3 * 2, a3 + 180, 40);
            }

            // High-tech holographic radar sweep scanning vertically over the eye
            int sweepRange = (int)(r1 * 0.85f);
            int sweepY = centerY - sweepRange + ((_tickCount * 4) % (sweepRange * 2));
            using (Pen sweepPen = new Pen(Color.FromArgb(60, ColCyan), 2))
            {
                g.DrawLine(sweepPen, centerX - sweepRange, sweepY, centerX + sweepRange, sweepY);
            }

            // --- HACKER BLINKING TITLE ---
            int alpha = (_tickCount % 40 < 20) ? 255 : 80; // Smooth pulse
            if (_tickCount % 13 == 0) alpha = 30; // Random glitch effect

            using (Font titleFont = new Font("Consolas", 32F, FontStyle.Bold | FontStyle.Italic))
            using (SolidBrush bTitle = new SolidBrush(Color.FromArgb(alpha, ColCyan)))
            {
                string titleText = "A N T A R Y A M I_";
                SizeF titleSize = g.MeasureString(titleText, titleFont);
                g.DrawString(titleText, titleFont, bTitle, new PointF(centerX - (titleSize.Width / 2), 50));
            }

            // --- 100% TRANSPARENT TEXT FIELD (DYNAMICALLY POSITIONED) ---
            int inputY = centerY + (int)(r3 * 1.25f);
            int inputX = centerX - 125;

            // Draw subtle underline
            using (Pen pLine = new Pen(Color.FromArgb(130, ColCyan), 1f))
            {
                g.DrawLine(pLine, inputX, inputY + 30, inputX + 250, inputY + 30);
            }

            // Draw Label
            using (Font fLabel = new Font("Consolas", 9F, FontStyle.Regular))
            using (SolidBrush bLabel = new SolidBrush(Color.FromArgb(140, 200, 230, 240)))
            {
                string labelText = _currentStep == LoginStep.Username ? "USERNAME" : "PASSWORD";
                g.DrawString(labelText, fLabel, bLabel, new Point(inputX, inputY - 18));
            }

            // Draw actual input text manually
            using (Font fInput = new Font("Consolas", 14F))
            using (SolidBrush bInput = new SolidBrush(ColCyan))
            {
                string renderText = _txtInput.Text;
                if (renderText == "Enter Username" || renderText == "Enter Password") {
                    renderText = ""; // Don't draw placeholder in custom render
                }
                
                if (_currentStep == LoginStep.Password)
                {
                    renderText = new string('•', renderText.Length);
                }

                // Blinking cursor
                string cursor = (_txtInput.Focused && _tickCount % 20 < 10) ? "_" : "";
                
                g.DrawString(renderText + cursor, fInput, bInput, new Point(inputX, inputY));
            }
        }

        private void TxtInput_TextChanged(object? sender, EventArgs e)
        {
            if (_currentStep == LoginStep.Username)
            {
                if (_txtInput.Text.Equals("ANTARYAMI", StringComparison.OrdinalIgnoreCase))
                {
                    Task.Run(() => Console.Beep(800, 200));
                    _currentStep = LoginStep.Password;
                    
                    // Unhook briefly to prevent loop
                    _txtInput.TextChanged -= TxtInput_TextChanged;
                    _txtInput.Clear();
                    _txtInput.TextChanged += TxtInput_TextChanged;
                }
            }
            else if (_currentStep == LoginStep.Password)
            {
                if (_txtInput.Text.Equals("Ragib@00100"))
                {
                    Task.Run(() => Console.Beep(1200, 500));
                    IsAuthenticated = true;
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
            }
        }
        
        private async void BtnLogin_Click(object? sender, EventArgs e)
        {
            // Optional manual login button handler (button is hidden, but kept for structure)
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_animTimer != null)
            {
                _animTimer.Stop();
                _animTimer.Dispose();
            }
            base.OnFormClosing(e);
        }
    }
}
