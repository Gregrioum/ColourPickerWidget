using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Colour Picker Widget")]
[assembly: AssemblyDescription("Native Glassmorphic Screen Eyedropper & Floating Palette Widget")]
[assembly: AssemblyCompany("Custom Desktop Widgets")]
[assembly: AssemblyProduct("Colour Picker Widget")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace ColourPickerWidget
{
    public class ColorItem
    {
        public string Hex { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public bool Pinned { get; set; }
        public string Timestamp { get; set; }

        public Color ToColor()
        {
            int r = Math.Max(0, Math.Min(255, R));
            int g = Math.Max(0, Math.Min(255, G));
            int b = Math.Max(0, Math.Min(255, B));
            return Color.FromArgb(255, r, g, b);
        }

        public static ColorItem FromColor(Color c, bool pinned = false)
        {
            return new ColorItem
            {
                Hex = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B),
                R = c.R,
                G = c.G,
                B = c.B,
                Pinned = pinned,
                Timestamp = DateTime.Now.ToString("HH:mm:ss")
            };
        }
    }

    public class AppConfig
    {
        public const int UNSET_COORD = -99999;

        public uint HotkeyModifiers = NativeMethods.MOD_ALT;
        public uint HotkeyKey = (uint)Keys.C;
        public string HotkeyText = "Alt + C";
        public bool AutoCopyOnPick = true;
        public string CopyFormat = "HEX";
        public bool AlwaysOnTop = true;
        public bool SnapToEdges = true;
        public double IdleOpacity = 0.88;
        public int WidgetX = UNSET_COORD;
        public int WidgetY = UNSET_COORD;
        public bool StartCollapsed = false;
        public List<ColorItem> History = new List<ColorItem>();

        private static string _cachedConfigPath;

        public static string ConfigPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedConfigPath))
                    return _cachedConfigPath;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string localCandidate = Path.Combine(baseDir, "widget_data.json");

                if (IsDirectoryWritable(baseDir))
                {
                    _cachedConfigPath = localCandidate;
                }
                else
                {
                    string appDataDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ColourPickerWidget");
                    try { Directory.CreateDirectory(appDataDir); } catch { }
                    _cachedConfigPath = Path.Combine(appDataDir, "widget_data.json");
                }
                return _cachedConfigPath;
            }
        }

        private static bool IsDirectoryWritable(string dirPath)
        {
            try
            {
                string probe = Path.Combine(dirPath, ".write_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Save()
        {
            try
            {
                IdleOpacity = Math.Max(0.40, Math.Min(1.00, IdleOpacity));

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendFormat("  \"HotkeyModifiers\": {0},\r\n", HotkeyModifiers);
                sb.AppendFormat("  \"HotkeyKey\": {0},\r\n", HotkeyKey);
                sb.AppendFormat("  \"HotkeyText\": \"{0}\",\r\n", Escape(HotkeyText));
                sb.AppendFormat("  \"AutoCopyOnPick\": {0},\r\n", AutoCopyOnPick ? "true" : "false");
                sb.AppendFormat("  \"CopyFormat\": \"{0}\",\r\n", Escape(CopyFormat));
                sb.AppendFormat("  \"AlwaysOnTop\": {0},\r\n", AlwaysOnTop ? "true" : "false");
                sb.AppendFormat("  \"SnapToEdges\": {0},\r\n", SnapToEdges ? "true" : "false");
                sb.AppendFormat("  \"IdleOpacity\": {0},\r\n", IdleOpacity.ToString(CultureInfo.InvariantCulture));
                sb.AppendFormat("  \"WidgetX\": {0},\r\n", WidgetX);
                sb.AppendFormat("  \"WidgetY\": {0},\r\n", WidgetY);
                sb.AppendFormat("  \"StartCollapsed\": {0},\r\n", StartCollapsed ? "true" : "false");
                sb.AppendLine("  \"History\": [");
                for (int i = 0; i < History.Count; i++)
                {
                    ColorItem item = History[i];
                    string comma = (i < History.Count - 1) ? "," : "";
                    sb.AppendFormat(
                        "    {{\"Hex\":\"{0}\",\"R\":{1},\"G\":{2},\"B\":{3},\"Pinned\":{4},\"Timestamp\":\"{5}\"}}{6}\r\n",
                        Escape(item.Hex),
                        item.R,
                        item.G,
                        item.B,
                        item.Pinned ? "true" : "false",
                        Escape(item.Timestamp ?? ""),
                        comma);
                }
                sb.AppendLine("  ]");
                sb.AppendLine("}");

                string targetPath = ConfigPath;
                string tempPath = targetPath + ".tmp";
                File.WriteAllText(tempPath, sb.ToString(), Encoding.UTF8);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);
            }
            catch
            {
            }
        }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            try
            {
                string path = ConfigPath;
                if (!File.Exists(path))
                {
                    AddDefaultStarterPalette(cfg);
                    return cfg;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                cfg.HotkeyModifiers = ParseUInt(json, "HotkeyModifiers", cfg.HotkeyModifiers);
                cfg.HotkeyKey = ParseUInt(json, "HotkeyKey", cfg.HotkeyKey);
                cfg.HotkeyText = ParseString(json, "HotkeyText", cfg.HotkeyText);
                cfg.AutoCopyOnPick = ParseBool(json, "AutoCopyOnPick", cfg.AutoCopyOnPick);
                cfg.CopyFormat = ParseString(json, "CopyFormat", cfg.CopyFormat);
                cfg.AlwaysOnTop = ParseBool(json, "AlwaysOnTop", cfg.AlwaysOnTop);
                cfg.SnapToEdges = ParseBool(json, "SnapToEdges", cfg.SnapToEdges);
                cfg.IdleOpacity = Math.Max(0.40, Math.Min(1.00, ParseDouble(json, "IdleOpacity", cfg.IdleOpacity)));

                int parsedX = ParseInt(json, "WidgetX", UNSET_COORD);
                int parsedY = ParseInt(json, "WidgetY", UNSET_COORD);
                // Migrate legacy -1 sentinel if both are -1
                if (parsedX == -1 && parsedY == -1)
                {
                    parsedX = UNSET_COORD;
                    parsedY = UNSET_COORD;
                }
                cfg.WidgetX = parsedX;
                cfg.WidgetY = parsedY;
                cfg.StartCollapsed = ParseBool(json, "StartCollapsed", cfg.StartCollapsed);

                MatchCollection matches = Regex.Matches(
                    json,
                    "\\{\"Hex\":\"(?<hex>[^\"]*)\",\"R\":(?<r>\\d+),\"G\":(?<g>\\d+),\"B\":(?<b>\\d+),\"Pinned\":(?<p>true|false),\"Timestamp\":\"(?<t>[^\"]*)\"\\}");

                foreach (Match m in matches)
                {
                    ColorItem item = new ColorItem
                    {
                        Hex = m.Groups["hex"].Value,
                        R = Math.Max(0, Math.Min(255, int.Parse(m.Groups["r"].Value))),
                        G = Math.Max(0, Math.Min(255, int.Parse(m.Groups["g"].Value))),
                        B = Math.Max(0, Math.Min(255, int.Parse(m.Groups["b"].Value))),
                        Pinned = (m.Groups["p"].Value == "true"),
                        Timestamp = m.Groups["t"].Value
                    };
                    cfg.History.Add(item);
                }
            }
            catch
            {
                if (cfg.History.Count == 0)
                    AddDefaultStarterPalette(cfg);
            }
            return cfg;
        }

        private static void AddDefaultStarterPalette(AppConfig cfg)
        {
            cfg.History.Add(ColorItem.FromColor(Color.FromArgb(99, 102, 241), false));
            cfg.History.Add(ColorItem.FromColor(Color.FromArgb(16, 185, 129), false));
            cfg.History.Add(ColorItem.FromColor(Color.FromArgb(244, 63, 94), false));
            cfg.History.Add(ColorItem.FromColor(Color.FromArgb(245, 158, 11), false));
            cfg.History.Add(ColorItem.FromColor(Color.FromArgb(14, 165, 233), false));
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static uint ParseUInt(string json, string key, uint def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\\d+)");
            uint val;
            return (m.Success && uint.TryParse(m.Groups[1].Value, out val)) ? val : def;
        }

        private static int ParseInt(string json, string key, int def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(-?\\d+)");
            int val;
            return (m.Success && int.TryParse(m.Groups[1].Value, out val)) ? val : def;
        }

        private static double ParseDouble(string json, string key, double def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*([0-9.]+)");
            double val;
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out val)) ? val : def;
        }

        private static bool ParseBool(string json, string key, bool def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*(true|false)");
            return m.Success ? (m.Groups[1].Value == "true") : def;
        }

        private static string ParseString(string json, string key, string def)
        {
            Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : def;
        }
    }

    public static class ColorUtils
    {
        public static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static string ToRgbString(Color c)
        {
            return string.Format("rgb({0}, {1}, {2})", c.R, c.G, c.B);
        }

        public static string ToHslString(Color c)
        {
            double h, s, l;
            RgbToHsl(c.R, c.G, c.B, out h, out s, out l);
            return string.Format("hsl({0}, {1}%, {2}%)",
                (int)Math.Round(h),
                (int)Math.Round(s * 100.0),
                (int)Math.Round(l * 100.0));
        }

        public static string ToCmykString(Color c)
        {
            if (c.R == 0 && c.G == 0 && c.B == 0)
                return "cmyk(0%, 0%, 0%, 100%)";

            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;
            double k = 1.0 - Math.Max(r, Math.Max(g, b));
            if (1.0 - k < 1e-6)
                return "cmyk(0%, 0%, 0%, 100%)";

            double cy = (1.0 - r - k) / (1.0 - k);
            double mg = (1.0 - g - k) / (1.0 - k);
            double yl = (1.0 - b - k) / (1.0 - k);
            return string.Format("cmyk({0}%, {1}%, {2}%, {3}%)",
                (int)Math.Round(cy * 100.0),
                (int)Math.Round(mg * 100.0),
                (int)Math.Round(yl * 100.0),
                (int)Math.Round(k * 100.0));
        }

        public static string FormatColor(Color c, string format)
        {
            string fmt = (format ?? "HEX").ToUpperInvariant();
            if (fmt == "RGB") return ToRgbString(c);
            if (fmt == "HSL") return ToHslString(c);
            if (fmt == "CMYK") return ToCmykString(c);
            return ToHex(c);
        }

        public static void RgbToHsl(int r255, int g255, int b255, out double h, out double s, out double l)
        {
            double r = r255 / 255.0;
            double g = g255 / 255.0;
            double b = b255 / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            h = 0.0;
            s = 0.0;
            l = (max + min) / 2.0;

            if (Math.Abs(max - min) > 0.0001)
            {
                double d = max - min;
                s = (l > 0.5) ? (d / (2.0 - max - min)) : (d / (max + min));
                if (Math.Abs(max - r) < 0.0001)
                    h = (g - b) / d + (g < b ? 6.0 : 0.0);
                else if (Math.Abs(max - g) < 0.0001)
                    h = (b - r) / d + 2.0;
                else
                    h = (r - g) / d + 4.0;
                h /= 6.0;
            }
            h *= 360.0;
        }

        public static Color HslToRgb(double h, double s, double l)
        {
            h = ((h % 360.0) + 360.0) % 360.0;
            s = Math.Max(0.0, Math.Min(1.0, s));
            l = Math.Max(0.0, Math.Min(1.0, l));

            double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = l - c / 2.0;
            double r1 = 0, g1 = 0, b1 = 0;

            if (h < 60.0) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120.0) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180.0) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240.0) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300.0) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            int r = Math.Max(0, Math.Min(255, (int)Math.Round((r1 + m) * 255.0)));
            int g = Math.Max(0, Math.Min(255, (int)Math.Round((g1 + m) * 255.0)));
            int b = Math.Max(0, Math.Min(255, (int)Math.Round((b1 + m) * 255.0)));
            return Color.FromArgb(255, r, g, b);
        }

        public static List<Color> GetShadesAndHarmonies(Color c)
        {
            double h, s, l;
            RgbToHsl(c.R, c.G, c.B, out h, out s, out l);
            return new List<Color>
            {
                HslToRgb(h, s, Math.Max(0.12, l - 0.28)),
                HslToRgb(h, s, Math.Max(0.20, l - 0.14)),
                c,
                HslToRgb(h, s, Math.Min(0.88, l + 0.16)),
                HslToRgb(h, s, Math.Min(0.94, l + 0.30)),
                HslToRgb((h + 180.0) % 360.0, s, l)
            };
        }

        public static GraphicsPath CreateRoundedRect(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
                return path;

            int d = radius * 2;
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;

            path.AddArc(rect.X, rect.Y, d, d, 180f, 90f);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270f, 90f);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0f, 90f);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }

    public static class NativeMethods
    {
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_ID = 9001;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public static readonly IntPtr HWND_BROADCAST = new IntPtr(0xFFFF);
        public static readonly int WM_SHOWEXISTING = RegisterWindowMessage("ColourPickerWidget_ShowExisting_Msg");

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static bool ApplyNativeWindowCornersAndBorder(IntPtr hwnd, Color accentColor)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int roundPref = 2; // DWMWCP_ROUND
                int hr = DwmSetWindowAttribute(hwnd, 33, ref roundPref, 4);

                int r = Math.Min(255, 70 + accentColor.R / 2);
                int g = Math.Min(255, 85 + accentColor.G / 2);
                int b = Math.Min(255, 125 + accentColor.B / 2);
                int colorRef = r | (g << 8) | (b << 16);
                DwmSetWindowAttribute(hwnd, 34, ref colorRef, 4);
                return (hr == 0);
            }
            catch
            {
                return false;
            }
        }
    }

    public class ScreenPickerOverlay : Form
    {
        private Bitmap _desktopBitmap;
        private Rectangle _virtualScreen;
        private Point _cursorPos;
        private Point _lastPaintedCursorPos;
        private int _zoomGridRadius = 5;
        private Color _currentColor = Color.Black;

        public Color PickedColor { get; private set; }

        public ScreenPickerOverlay()
        {
            _virtualScreen = SystemInformation.VirtualScreen;
            _desktopBitmap = new Bitmap(_virtualScreen.Width, _virtualScreen.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(_desktopBitmap))
            {
                g.CopyFromScreen(
                    _virtualScreen.Left,
                    _virtualScreen.Top,
                    0,
                    0,
                    _virtualScreen.Size,
                    CopyPixelOperation.SourceCopy);
            }

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = _virtualScreen;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            KeyPreview = true;

            _cursorPos = Cursor.Position;
            _lastPaintedCursorPos = _cursorPos;
            UpdateCurrentPixel();
        }

        private void UpdateCurrentPixel()
        {
            if (_desktopBitmap == null) return;
            int x = _cursorPos.X - _virtualScreen.Left;
            int y = _cursorPos.Y - _virtualScreen.Top;
            x = Math.Max(0, Math.Min(_desktopBitmap.Width - 1, x));
            y = Math.Max(0, Math.Min(_desktopBitmap.Height - 1, y));
            _currentColor = _desktopBitmap.GetPixel(x, y);
        }

        private Rectangle GetLoupeDirtyBounds(Point screenPt)
        {
            int localX = screenPt.X - _virtualScreen.Left;
            int localY = screenPt.Y - _virtualScreen.Top;
            return new Rectangle(localX - 240, localY - 260, 480, 520);
        }

        private void InvalidateLoupeMove(Point oldPos, Point newPos)
        {
            Rectangle r1 = GetLoupeDirtyBounds(oldPos);
            Rectangle r2 = GetLoupeDirtyBounds(newPos);
            Invalidate(Rectangle.Union(r1, r2));
            _lastPaintedCursorPos = newPos;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point oldPos = _cursorPos;
            _cursorPos = new Point(e.X + _virtualScreen.Left, e.Y + _virtualScreen.Top);
            UpdateCurrentPixel();
            InvalidateLoupeMove(oldPos, _cursorPos);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta > 0 && _zoomGridRadius > 3)
                _zoomGridRadius--;
            else if (e.Delta < 0 && _zoomGridRadius < 10)
                _zoomGridRadius++;
            Invalidate(GetLoupeDirtyBounds(_cursorPos));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                UpdateCurrentPixel();
                PickedColor = _currentColor;
                DialogResult = DialogResult.OK;
                Close();
            }
            else if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                UpdateCurrentPixel();
                PickedColor = _currentColor;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            int dx = 0, dy = 0;
            if (e.KeyCode == Keys.Left) dx = -1;
            else if (e.KeyCode == Keys.Right) dx = 1;
            else if (e.KeyCode == Keys.Up) dy = -1;
            else if (e.KeyCode == Keys.Down) dy = 1;

            if (dx != 0 || dy != 0)
            {
                Point oldPos = _cursorPos;
                int nx = Math.Max(_virtualScreen.Left, Math.Min(_virtualScreen.Right - 1, _cursorPos.X + dx));
                int ny = Math.Max(_virtualScreen.Top, Math.Min(_virtualScreen.Bottom - 1, _cursorPos.Y + dy));
                _cursorPos = new Point(nx, ny);
                Cursor.Position = _cursorPos;
                UpdateCurrentPixel();
                InvalidateLoupeMove(oldPos, _cursorPos);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_desktopBitmap != null)
            {
                Rectangle clip = e.ClipRectangle;
                if (clip.Width > 0 && clip.Height > 0)
                    g.DrawImage(_desktopBitmap, clip, clip, GraphicsUnit.Pixel);
                else
                    g.DrawImageUnscaled(_desktopBitmap, 0, 0);
            }

            int cx = _cursorPos.X - _virtualScreen.Left;
            int cy = _cursorPos.Y - _virtualScreen.Top;
            int loupeSize = 154;
            int hudHeight = 62;
            int totalW = loupeSize;
            int totalH = loupeSize + hudHeight + 8;

            int lx = cx + 24;
            int ly = cy + 24;
            if (lx + totalW + 16 > Width) lx = cx - totalW - 24;
            if (ly + totalH + 16 > Height) ly = cy - totalH - 24;

            Rectangle loupeRect = new Rectangle(lx, ly, loupeSize, loupeSize);
            int cells = _zoomGridRadius * 2 + 1;
            float cellSize = (float)loupeSize / cells;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            {
                g.FillEllipse(shadowBrush, lx + 3, ly + 5, loupeSize, loupeSize);
            }

            if (_desktopBitmap != null)
            {
                using (GraphicsPath circlePath = new GraphicsPath())
                {
                    circlePath.AddEllipse(loupeRect);
                    Region prevClip = g.Clip;
                    g.SetClip(circlePath, CombineMode.Replace);
                    g.SmoothingMode = SmoothingMode.None;

                    for (int gy = -_zoomGridRadius; gy <= _zoomGridRadius; gy++)
                    {
                        for (int gx = -_zoomGridRadius; gx <= _zoomGridRadius; gx++)
                        {
                            int px = Math.Max(0, Math.Min(_desktopBitmap.Width - 1, cx + gx));
                            int py = Math.Max(0, Math.Min(_desktopBitmap.Height - 1, cy + gy));
                            Color pc = _desktopBitmap.GetPixel(px, py);

                            float rx = lx + (gx + _zoomGridRadius) * cellSize;
                            float ry = ly + (gy + _zoomGridRadius) * cellSize;
                            using (SolidBrush cb = new SolidBrush(pc))
                            using (Pen gridPen = new Pen(Color.FromArgb(35, 255, 255, 255), 1f))
                            {
                                g.FillRectangle(cb, rx, ry, cellSize + 0.8f, cellSize + 0.8f);
                                g.DrawRectangle(gridPen, rx, ry, cellSize, cellSize);
                            }
                        }
                    }

                    float centerRx = lx + _zoomGridRadius * cellSize;
                    float centerRy = ly + _zoomGridRadius * cellSize;
                    using (Pen outerCross = new Pen(Color.Black, 2.5f))
                    using (Pen innerCross = new Pen(Color.White, 1.2f))
                    {
                        g.DrawRectangle(outerCross, centerRx - 1f, centerRy - 1f, cellSize + 2f, cellSize + 2f);
                        g.DrawRectangle(innerCross, centerRx, centerRy, cellSize, cellSize);
                    }

                    g.SetClip(prevClip, CombineMode.Replace);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                }
            }

            using (Pen ringOuter = new Pen(Color.FromArgb(230, 20, 22, 30), 6f))
            using (Pen ringAccent = new Pen(_currentColor, 3.5f))
            {
                g.DrawEllipse(ringOuter, loupeRect);
                g.DrawEllipse(ringAccent, loupeRect);
            }

            Rectangle hudRect = new Rectangle(lx - 8, ly + loupeSize + 8, loupeSize + 16, hudHeight);
            using (GraphicsPath hudPath = ColorUtils.CreateRoundedRect(hudRect, 10))
            using (SolidBrush hudBg = new SolidBrush(Color.FromArgb(240, 22, 24, 33)))
            using (Pen hudBorder = new Pen(Color.FromArgb(100, 255, 255, 255), 1f))
            {
                g.FillPath(hudBg, hudPath);
                g.DrawPath(hudBorder, hudPath);
            }

            Rectangle swatchRect = new Rectangle(hudRect.X + 10, hudRect.Y + 10, 26, 26);
            using (GraphicsPath swPath = ColorUtils.CreateRoundedRect(swatchRect, 6))
            using (SolidBrush swBrush = new SolidBrush(_currentColor))
            using (Pen swBorder = new Pen(Color.FromArgb(140, 255, 255, 255), 1f))
            {
                g.FillPath(swBrush, swPath);
                g.DrawPath(swBorder, swPath);
            }

            string hexText = ColorUtils.ToHex(_currentColor);
            string rgbText = string.Format("R:{0} G:{1} B:{2}", _currentColor.R, _currentColor.G, _currentColor.B);
            string hintText = "Click: Pick | Scroll: Zoom | Arrows: 1px";

            using (Font fHex = new Font("Consolas", 11.5f, FontStyle.Bold))
            using (Font fRgb = new Font("Segoe UI", 7.8f, FontStyle.Regular))
            using (Font fHint = new Font("Segoe UI", 6.8f, FontStyle.Regular))
            using (SolidBrush bWhite = new SolidBrush(Color.White))
            using (SolidBrush bSub = new SolidBrush(Color.FromArgb(180, 195, 210)))
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.DrawString(hexText, fHex, bWhite, hudRect.X + 42, hudRect.Y + 7);
                g.DrawString(rgbText, fRgb, bSub, hudRect.X + 43, hudRect.Y + 25);
                g.DrawString(hintText, fHint, bSub, hudRect.X + 8, hudRect.Y + 44);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _desktopBitmap != null)
            {
                _desktopBitmap.Dispose();
                _desktopBitmap = null;
            }
            base.Dispose(disposing);
        }
    }

    public class SettingsDialog : Form
    {
        private const string STARTUP_REG_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string STARTUP_APP_NAME = "ColourPickerWidget";

        private AppConfig _config;
        private TextBox _txtHotkey;
        private uint _tempModifiers;
        private uint _tempKey;
        private string _tempHotkeyText;
        private CheckBox _chkAutoCopy;
        private ComboBox _cmbFormat;
        private CheckBox _chkTopMost;
        private CheckBox _chkSnapEdges;
        private CheckBox _chkStartup;
        private TrackBar _trkOpacity;
        private Label _lblOpacityVal;

        public SettingsDialog(AppConfig config)
        {
            _config = config;
            _tempModifiers = config.HotkeyModifiers;
            _tempKey = config.HotkeyKey;
            _tempHotkeyText = config.HotkeyText;

            Text = "Colour Picker Settings";
            Size = new Size(370, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 26, 35);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Regular);
            TopMost = true;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            int y = 18;
            Label lblShortcut = new Label
            {
                Text = "Global Shortcut (Click box and press 2 keys):",
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 210, 230)
            };
            Controls.Add(lblShortcut);

            y += 24;
            _txtHotkey = new TextBox
            {
                Text = _tempHotkeyText,
                Location = new Point(20, y),
                Size = new Size(230, 28),
                ReadOnly = true,
                BackColor = Color.FromArgb(35, 38, 52),
                ForeColor = Color.FromArgb(129, 140, 248),
                Font = new Font("Consolas", 10.5f, FontStyle.Bold),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center
            };
            _txtHotkey.KeyDown += TxtHotkey_KeyDown;
            Controls.Add(_txtHotkey);

            Button btnResetHotkey = new Button
            {
                Text = "Default (Alt+C)",
                Location = new Point(258, y - 1),
                Size = new Size(88, 28),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 49, 66),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8f)
            };
            btnResetHotkey.FlatAppearance.BorderSize = 0;
            btnResetHotkey.Click += (s, e) =>
            {
                _tempModifiers = NativeMethods.MOD_ALT;
                _tempKey = (uint)Keys.C;
                _tempHotkeyText = "Alt + C";
                _txtHotkey.Text = _tempHotkeyText;
            };
            Controls.Add(btnResetHotkey);

            y += 42;
            Label lblFmt = new Label
            {
                Text = "Default Auto-Copy Format:",
                Location = new Point(20, y + 4),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 210, 230)
            };
            Controls.Add(lblFmt);

            _cmbFormat = new ComboBox
            {
                Location = new Point(200, y),
                Size = new Size(146, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(35, 38, 52),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _cmbFormat.Items.AddRange(new object[] { "HEX", "RGB", "HSL", "CMYK" });
            _cmbFormat.SelectedItem = _cmbFormat.Items.Contains(_config.CopyFormat) ? _config.CopyFormat : "HEX";
            Controls.Add(_cmbFormat);

            y += 38;
            _chkAutoCopy = new CheckBox
            {
                Text = "Automatically copy code to clipboard on pick",
                Location = new Point(20, y),
                AutoSize = true,
                Checked = _config.AutoCopyOnPick
            };
            Controls.Add(_chkAutoCopy);

            y += 30;
            _chkTopMost = new CheckBox
            {
                Text = "Keep floating widget Always on Top of windows",
                Location = new Point(20, y),
                AutoSize = true,
                Checked = _config.AlwaysOnTop
            };
            Controls.Add(_chkTopMost);

            y += 30;
            _chkSnapEdges = new CheckBox
            {
                Text = "Snap floating widget to screen edges when dragged",
                Location = new Point(20, y),
                AutoSize = true,
                Checked = _config.SnapToEdges
            };
            Controls.Add(_chkSnapEdges);

            y += 30;
            _chkStartup = new CheckBox
            {
                Text = "Launch automatically when Windows starts",
                Location = new Point(20, y),
                AutoSize = true,
                Checked = IsStartupEnabled()
            };
            Controls.Add(_chkStartup);

            y += 36;
            Label lblOpacity = new Label
            {
                Text = "Widget Opacity:",
                Location = new Point(20, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 210, 230)
            };
            Controls.Add(lblOpacity);

            int opPct = Math.Max(40, Math.Min(100, (int)Math.Round(_config.IdleOpacity * 100.0)));
            _lblOpacityVal = new Label
            {
                Text = string.Format("{0}%", opPct),
                Location = new Point(300, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(129, 140, 248)
            };
            Controls.Add(_lblOpacityVal);

            y += 22;
            _trkOpacity = new TrackBar
            {
                Location = new Point(20, y),
                Size = new Size(320, 32),
                Minimum = 40,
                Maximum = 100,
                TickFrequency = 10,
                Value = opPct
            };
            _trkOpacity.Scroll += (s, e) =>
            {
                _lblOpacityVal.Text = _trkOpacity.Value + "%";
            };
            Controls.Add(_trkOpacity);

            y += 50;
            Button btnSave = new Button
            {
                Text = "Save Settings",
                Location = new Point(115, y),
                Size = new Size(115, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(99, 102, 241),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold)
            };
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.Click += BtnSave_Click;
            Controls.Add(btnSave);

            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(240, y),
                Size = new Size(100, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 49, 66),
                ForeColor = Color.White
            };
            btnCancel.FlatAppearance.BorderSize = 0;
            btnCancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(btnCancel);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.ApplyNativeWindowCornersAndBorder(Handle, Color.FromArgb(99, 102, 241));
        }

        private void TxtHotkey_KeyDown(object sender, KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                return;
            }

            uint mods = 0;
            List<string> parts = new List<string>();
            if (e.Control) { mods |= NativeMethods.MOD_CONTROL; parts.Add("Ctrl"); }
            if (e.Alt) { mods |= NativeMethods.MOD_ALT; parts.Add("Alt"); }
            if (e.Shift) { mods |= NativeMethods.MOD_SHIFT; parts.Add("Shift"); }

            if (mods == 0)
            {
                _txtHotkey.Text = "Hold Alt/Ctrl/Shift + Key";
                return;
            }

            if (mods == NativeMethods.MOD_SHIFT && e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
            {
                DialogResult dr = MessageBox.Show(
                    "Note: Using 'Shift + " + e.KeyCode + "' will trigger the Color Picker whenever you type a capital '" + e.KeyCode + "'.\n\nWould you like to use 'Alt + " + e.KeyCode + "' instead?",
                    "Shortcut Conflict Warning",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);
                if (dr == DialogResult.Yes)
                {
                    mods = NativeMethods.MOD_ALT;
                    parts.Clear();
                    parts.Add("Alt");
                }
            }

            parts.Add(FormatKeyName(e.KeyCode));
            _tempModifiers = mods;
            _tempKey = (uint)e.KeyCode;
            _tempHotkeyText = string.Join(" + ", parts.ToArray());
            _txtHotkey.Text = _tempHotkeyText;
        }

        private string FormatKeyName(Keys k)
        {
            if (k == Keys.Oemtilde) return "`";
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            return k.ToString();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            _config.HotkeyModifiers = _tempModifiers;
            _config.HotkeyKey = _tempKey;
            _config.HotkeyText = _tempHotkeyText;
            _config.AutoCopyOnPick = _chkAutoCopy.Checked;
            _config.CopyFormat = (_cmbFormat.SelectedItem ?? "HEX").ToString();
            _config.AlwaysOnTop = _chkTopMost.Checked;
            _config.SnapToEdges = _chkSnapEdges.Checked;
            _config.IdleOpacity = _trkOpacity.Value / 100.0;
            SetStartup(_chkStartup.Checked);
            _config.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, false))
                {
                    if (rk != null)
                        return rk.GetValue(STARTUP_APP_NAME) != null;
                }
            }
            catch { }
            return false;
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, true))
                {
                    if (rk != null)
                    {
                        if (enable)
                            rk.SetValue(STARTUP_APP_NAME, "\"" + Application.ExecutablePath + "\"");
                        else if (rk.GetValue(STARTUP_APP_NAME) != null)
                            rk.DeleteValue(STARTUP_APP_NAME, false);
                    }
                }
            }
            catch { }
        }
    }

    public class MainWidgetForm : Form
    {
        private const int EXPANDED_WIDTH = 330;
        private const int EXPANDED_HEIGHT = 490;
        private const int MINI_WIDTH = 152;
        private const int MINI_HEIGHT = 48;
        private const int MAX_HISTORY = 20;

        private AppConfig _config;
        private bool _isCollapsed;
        private Color _activeColor = Color.FromArgb(99, 102, 241);
        private bool _isDragging;
        private Point _dragStartMouse;
        private Point _dragStartForm;
        private bool _hasMovedDuringDrag;
        private bool _isPicking;
        private bool _isSettingsOpen;

        private Rectangle _rectPickBtn;
        private Rectangle _rectCollapseBtn;
        private Rectangle _rectSettingsBtn;
        private Rectangle _rectHideBtn;
        private Rectangle _rectClearHistoryBtn;
        private Dictionary<string, Rectangle> _formatCopyRects = new Dictionary<string, Rectangle>();
        private List<KeyValuePair<Rectangle, Color>> _harmonyRects = new List<KeyValuePair<Rectangle, Color>>();
        private List<KeyValuePair<Rectangle, int>> _historySwatchRects = new List<KeyValuePair<Rectangle, int>>();
        private List<KeyValuePair<Rectangle, int>> _historyPinRects = new List<KeyValuePair<Rectangle, int>>();
        private Rectangle _rectMiniPickCircle;
        private Rectangle _rectMiniExpandArea;

        private Point _mousePos = new Point(-1, -1);
        private string _toastMessage = "";
        private System.Windows.Forms.Timer _toastTimer;
        private ToolTip _toolTip;
        private string _lastTooltipText = "";
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private bool _hasNativeDwmCorners;

        public MainWidgetForm()
        {
            _config = AppConfig.Load();
            _config.IdleOpacity = Math.Max(0.40, Math.Min(1.00, _config.IdleOpacity));

            if (_config.History.Count > 0)
                _activeColor = _config.History[0].ToColor();

            _isCollapsed = _config.StartCollapsed;
            Text = "Colour Picker Widget";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(16, 19, 28);
            DoubleBuffered = true;
            ShowInTaskbar = false;
            TopMost = _config.AlwaysOnTop;
            Opacity = _config.IdleOpacity;

            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            ApplyDimensionsAndRegion();

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (_config.WidgetX != AppConfig.UNSET_COORD &&
                _config.WidgetY != AppConfig.UNSET_COORD &&
                IsPointVisibleOnAnyScreen(new Point(_config.WidgetX, _config.WidgetY)))
            {
                Location = new Point(_config.WidgetX, _config.WidgetY);
            }
            else
            {
                Location = new Point(wa.Right - Width - 28, wa.Top + 60);
            }

            _toastTimer = new System.Windows.Forms.Timer { Interval = 1800 };
            _toastTimer.Tick += (s, e) =>
            {
                _toastMessage = "";
                _toastTimer.Stop();
                Invalidate();
            };

            _toolTip = new ToolTip
            {
                InitialDelay = 250,
                ReshowDelay = 100,
                AutoPopDelay = 5000
            };

            InitializeTrayIcon();
        }

        private bool IsPointVisibleOnAnyScreen(Point p)
        {
            foreach (Screen scr in Screen.AllScreens)
            {
                Rectangle wa = scr.WorkingArea;
                if (wa.Contains(new Point(p.X + 20, p.Y + 20)))
                    return true;
            }
            return false;
        }

        private void ApplyDimensionsAndRegion()
        {
            if (Region != null)
            {
                Region oldRgn = Region;
                Region = null;
                oldRgn.Dispose();
            }

            Size = _isCollapsed
                ? new Size(MINI_WIDTH, MINI_HEIGHT)
                : new Size(EXPANDED_WIDTH, EXPANDED_HEIGHT);

            if (IsHandleCreated)
            {
                _hasNativeDwmCorners = NativeMethods.ApplyNativeWindowCornersAndBorder(Handle, _activeColor);
                if (!_hasNativeDwmCorners)
                {
                    IntPtr hRgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 16, 16);
                    if (hRgn != IntPtr.Zero)
                    {
                        Region = Region.FromHrgn(hRgn);
                        NativeMethods.DeleteObject(hRgn);
                    }
                }
            }

            KeepInsideScreenBounds();
            Invalidate();
        }

        private void KeepInsideScreenBounds()
        {
            Screen scr = Screen.FromControl(this);
            Rectangle wa = scr.WorkingArea;
            int x = Left;
            int y = Top;
            if (x + Width > wa.Right) x = wa.Right - Width - 8;
            if (y + Height > wa.Bottom) y = wa.Bottom - Height - 8;
            if (x < wa.Left) x = wa.Left + 8;
            if (y < wa.Top) y = wa.Top + 8;
            Location = new Point(x, y);
        }

        private Icon CreateDynamicAppIcon(Color c)
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(1, 1, 30, 30);
                using (GraphicsPath gp = ColorUtils.CreateRoundedRect(r, 7))
                using (LinearGradientBrush bg = new LinearGradientBrush(
                    Rectangle.Inflate(r, 1, 1),
                    Color.FromArgb(28, 33, 43),
                    Color.FromArgb(15, 18, 24),
                    LinearGradientMode.ForwardDiagonal))
                using (Pen borderPen = new Pen(Color.FromArgb(190, 71, 85, 105), 1.4f))
                {
                    g.FillPath(bg, gp);
                    g.DrawPath(borderPen, gp);
                }

                using (SolidBrush cb = new SolidBrush(c))
                using (Pen ringPen = new Pen(Color.FromArgb(235, 241, 245, 249), 1.8f))
                {
                    g.FillEllipse(cb, 7, 7, 18, 18);
                    g.DrawEllipse(ringPen, 7, 7, 18, 18);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        return (Icon)tempIcon.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }

        private void InitializeTrayIcon()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Pick Colour Now (" + _config.HotkeyText + ")", null, (s, e) => TriggerScreenPicker());
            _trayMenu.Items.Add("Show / Expand Widget", null, (s, e) => ShowAndRestoreWidget(false));
            _trayMenu.Items.Add("Floating Mini Icon Mode", null, (s, e) => ShowAndRestoreWidget(true));
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Settings...", null, (s, e) => OpenSettings());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit Colour Picker", null, (s, e) => ExitApplication());

            _trayIcon = new NotifyIcon
            {
                Icon = CreateDynamicAppIcon(_activeColor),
                Text = TruncateNotifyText("Colour Picker Widget (" + _config.HotkeyText + ")"),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (!Visible)
                        ShowAndRestoreWidget(_isCollapsed);
                    else
                        TriggerScreenPicker();
                }
            };
        }

        private static string TruncateNotifyText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Colour Picker Widget";
            return (s.Length > 60) ? s.Substring(0, 60) : s;
        }

        private void UpdateTrayIcon()
        {
            if (IsHandleCreated)
            {
                _hasNativeDwmCorners = NativeMethods.ApplyNativeWindowCornersAndBorder(Handle, _activeColor);
            }
            if (_trayIcon != null)
            {
                Icon oldIcon = _trayIcon.Icon;
                _trayIcon.Icon = CreateDynamicAppIcon(_activeColor);
                if (oldIcon != null)
                {
                    try { oldIcon.Dispose(); } catch { }
                }
                _trayIcon.Text = TruncateNotifyText(string.Format("Colour Picker ({0}) - Last: {1}", _config.HotkeyText, ColorUtils.ToHex(_activeColor)));
                if (_trayMenu != null && _trayMenu.Items.Count > 0)
                {
                    _trayMenu.Items[0].Text = "Pick Colour Now (" + _config.HotkeyText + ")";
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _hasNativeDwmCorners = NativeMethods.ApplyNativeWindowCornersAndBorder(Handle, _activeColor);
            if (!_hasNativeDwmCorners)
            {
                IntPtr hRgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 16, 16);
                if (hRgn != IntPtr.Zero)
                {
                    Region = Region.FromHrgn(hRgn);
                    NativeMethods.DeleteObject(hRgn);
                }
            }
            RegisterGlobalShortcut();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SavePositionAndState();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            base.OnFormClosing(e);
        }

        private void SavePositionAndState()
        {
            if (_config != null)
            {
                _config.WidgetX = Left;
                _config.WidgetY = Top;
                _config.StartCollapsed = _isCollapsed;
                _config.Save();
            }
        }

        private void RegisterGlobalShortcut()
        {
            if (!IsHandleCreated) return;
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID);
            bool ok = NativeMethods.RegisterHotKey(Handle, NativeMethods.HOTKEY_ID, _config.HotkeyModifiers, _config.HotkeyKey);
            if (!ok)
            {
                ShowToast("Shortcut busy! Change in Settings");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
            {
                TriggerScreenPicker();
                return;
            }
            if (NativeMethods.WM_SHOWEXISTING != 0 && m.Msg == NativeMethods.WM_SHOWEXISTING)
            {
                ShowAndRestoreWidget(false);
                return;
            }
            base.WndProc(ref m);
        }

        public void TriggerScreenPicker()
        {
            if (_isPicking || _isSettingsOpen)
                return;

            _isPicking = true;
            bool wasVisible = Visible;
            double savedOpacity = _config.IdleOpacity;
            Color? picked = null;

            try
            {
                if (wasVisible)
                {
                    // Set Opacity = 0 first so DWM hides immediately with zero fade-out ghosting on screenshot
                    Opacity = 0.0;
                    Visible = false;
                    Application.DoEvents();
                    Thread.Sleep(25);
                }

                using (ScreenPickerOverlay overlay = new ScreenPickerOverlay())
                {
                    if (overlay.ShowDialog() == DialogResult.OK)
                    {
                        picked = overlay.PickedColor;
                    }
                }
            }
            finally
            {
                Opacity = savedOpacity;
                if (wasVisible || picked.HasValue)
                {
                    Visible = true;
                    TopMost = _config.AlwaysOnTop;
                    BringToFront();
                }
                _isPicking = false;
            }

            if (picked.HasValue)
            {
                RecordPickedColor(picked.Value);
            }
        }

        private void RecordPickedColor(Color c)
        {
            _activeColor = c;
            string hex = ColorUtils.ToHex(c);
            int existingIdx = _config.History.FindIndex(x => string.Equals(x.Hex, hex, StringComparison.OrdinalIgnoreCase));
            bool wasPinned = false;
            if (existingIdx >= 0)
            {
                wasPinned = _config.History[existingIdx].Pinned;
                _config.History.RemoveAt(existingIdx);
            }
            _config.History.Insert(0, ColorItem.FromColor(c, wasPinned));

            while (_config.History.Count > MAX_HISTORY)
            {
                int removeIdx = -1;
                for (int i = _config.History.Count - 1; i >= 0; i--)
                {
                    if (!_config.History[i].Pinned)
                    {
                        removeIdx = i;
                        break;
                    }
                }
                if (removeIdx == -1)
                    removeIdx = _config.History.Count - 1;
                _config.History.RemoveAt(removeIdx);
            }

            _config.Save();
            UpdateTrayIcon();

            if (_config.AutoCopyOnPick)
            {
                string formatted = ColorUtils.FormatColor(c, _config.CopyFormat);
                CopyToClipboard(formatted, "Picked & Copied " + formatted);
            }
            else
            {
                ShowToast("Picked " + hex);
            }
            Invalidate();
        }

        private void CopyToClipboard(string text, string toast = null)
        {
            bool copied = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    copied = true;
                    break;
                }
                catch
                {
                    Thread.Sleep(20);
                }
            }

            if (copied)
                ShowToast(toast ?? ("Copied " + text));
            else
                ShowToast("Clipboard busy");
        }

        private void ShowToast(string msg)
        {
            _toastMessage = msg;
            _toastTimer.Stop();
            _toastTimer.Start();
            Invalidate();
        }

        private void ShowAndRestoreWidget(bool collapsedMode)
        {
            _isCollapsed = collapsedMode;
            _config.StartCollapsed = _isCollapsed;
            _config.Save();
            Opacity = _config.IdleOpacity;
            Visible = true;
            ApplyDimensionsAndRegion();
            TopMost = _config.AlwaysOnTop;
            BringToFront();
            Activate();
        }

        private void OpenSettings()
        {
            if (_isSettingsOpen || _isPicking)
                return;

            _isSettingsOpen = true;
            try
            {
                using (SettingsDialog dlg = new SettingsDialog(_config))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        TopMost = _config.AlwaysOnTop;
                        Opacity = _config.IdleOpacity;
                        RegisterGlobalShortcut();
                        UpdateTrayIcon();
                        ShowToast("Settings saved (" + _config.HotkeyText + ")");
                        Invalidate();
                    }
                }
            }
            finally
            {
                _isSettingsOpen = false;
            }
        }

        private void ExitApplication()
        {
            SavePositionAndState();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            Application.Exit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (_isCollapsed)
                PaintMiniFloatingIcon(g);
            else
                PaintExpandedWidget(g);
        }

        private void PaintMiniFloatingIcon(Graphics g)
        {
            Rectangle fullRect = new Rectangle(0, 0, Width, Height);
            Color cTop = Color.FromArgb(
                255,
                Math.Min(255, 22 + _activeColor.R / 7),
                Math.Min(255, 26 + _activeColor.G / 7),
                Math.Min(255, 38 + _activeColor.B / 7));
            Color cBot = Color.FromArgb(255, 13, 16, 24);

            using (LinearGradientBrush bg = new LinearGradientBrush(fullRect, cTop, cBot, LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(bg, fullRect);
            }

            Rectangle sheenRect = new Rectangle(0, 0, Width, Height / 2);
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                sheenRect,
                Color.FromArgb(42, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(sheen, sheenRect);
            }

            if (!_hasNativeDwmCorners)
            {
                Color bCol = Color.FromArgb(
                    150,
                    Math.Min(255, 110 + _activeColor.R / 2),
                    Math.Min(255, 125 + _activeColor.G / 2),
                    Math.Min(255, 165 + _activeColor.B / 2));
                using (Pen bPen = new Pen(bCol, 1f))
                {
                    g.DrawRectangle(bPen, 0, 0, Width - 1, Height - 1);
                }
            }

            _rectMiniPickCircle = new Rectangle(7, 7, 34, 34);
            bool hoverPick = _rectMiniPickCircle.Contains(_mousePos);
            using (GraphicsPath gp = ColorUtils.CreateRoundedRect(_rectMiniPickCircle, 6))
            using (SolidBrush sb = new SolidBrush(_activeColor))
            using (Pen p = new Pen(hoverPick ? Color.White : Color.FromArgb(190, 255, 255, 255), hoverPick ? 2f : 1.2f))
            {
                g.FillPath(sb, gp);
                g.DrawPath(p, gp);
            }

            int cx = _rectMiniPickCircle.X + _rectMiniPickCircle.Width / 2;
            int cy = _rectMiniPickCircle.Y + _rectMiniPickCircle.Height / 2;
            Color iconCol = GetContrastTextColor(_activeColor);
            using (Pen crossPen = new Pen(iconCol, 1.6f))
            {
                g.DrawEllipse(crossPen, cx - 5, cy - 5, 10, 10);
                g.DrawLine(crossPen, cx - 8, cy, cx - 3, cy);
                g.DrawLine(crossPen, cx + 3, cy, cx + 8, cy);
                g.DrawLine(crossPen, cx, cy - 8, cx, cy - 3);
                g.DrawLine(crossPen, cx, cy + 3, cx, cy + 8);
            }

            _rectMiniExpandArea = new Rectangle(46, 4, Width - 52, Height - 8);
            string hex = ColorUtils.ToHex(_activeColor);
            using (Font fHex = new Font("Consolas", 10f, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 7f, FontStyle.Regular))
            using (SolidBrush bMain = new SolidBrush(Color.FromArgb(245, 248, 255)))
            using (SolidBrush bSub = new SolidBrush(Color.FromArgb(180, 195, 220)))
            {
                g.DrawString(hex, fHex, bMain, 47f, 8f);
                string sub = string.IsNullOrEmpty(_toastMessage) ? (_config.HotkeyText + " • Open") : "Copied!";
                g.DrawString(sub, fSub, bSub, 48f, 25f);
            }
        }

        private void PaintExpandedWidget(Graphics g)
        {
            Rectangle fullRect = new Rectangle(0, 0, Width, Height);
            Color cTop = Color.FromArgb(
                255,
                Math.Min(255, 20 + _activeColor.R / 8),
                Math.Min(255, 24 + _activeColor.G / 8),
                Math.Min(255, 36 + _activeColor.B / 8));
            Color cBot = Color.FromArgb(255, 12, 15, 23);

            using (LinearGradientBrush bg = new LinearGradientBrush(fullRect, cTop, cBot, LinearGradientMode.ForwardDiagonal))
            {
                g.FillRectangle(bg, fullRect);
            }

            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(-40, -30, 230, 190);
                using (PathGradientBrush pgb = new PathGradientBrush(glowPath))
                {
                    pgb.CenterColor = Color.FromArgb(46, _activeColor.R, _activeColor.G, _activeColor.B);
                    pgb.SurroundColors = new Color[] { Color.FromArgb(0, _activeColor.R, _activeColor.G, _activeColor.B) };
                    g.FillPath(pgb, glowPath);
                }
            }

            Rectangle sheenRect = new Rectangle(0, 0, Width, 95);
            using (LinearGradientBrush sheen = new LinearGradientBrush(
                sheenRect,
                Color.FromArgb(30, 255, 255, 255),
                Color.FromArgb(0, 255, 255, 255),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(sheen, sheenRect);
            }

            if (!_hasNativeDwmCorners)
            {
                Color bCol = Color.FromArgb(
                    145,
                    Math.Min(255, 100 + _activeColor.R / 2),
                    Math.Min(255, 115 + _activeColor.G / 2),
                    Math.Min(255, 155 + _activeColor.B / 2));
                using (Pen bPen = new Pen(bCol, 1f))
                {
                    g.DrawRectangle(bPen, 0, 0, Width - 1, Height - 1);
                }
            }

            using (SolidBrush outerDot = new SolidBrush(Color.FromArgb(80, _activeColor)))
            using (SolidBrush innerDot = new SolidBrush(_activeColor))
            using (Pen dotRing = new Pen(Color.FromArgb(190, 255, 255, 255), 1f))
            {
                g.FillEllipse(outerDot, 14, 13, 16, 16);
                g.FillEllipse(innerDot, 16, 15, 12, 12);
                g.DrawEllipse(dotRing, 16, 15, 12, 12);
            }

            using (Font fTitle = new Font("Segoe UI", 9.5f, FontStyle.Bold))
            using (SolidBrush bTitle = new SolidBrush(Color.FromArgb(242, 246, 255)))
            {
                g.DrawString("Colour Picker", fTitle, bTitle, 34f, 12f);
            }

            string hk = _config.HotkeyText;
            using (Font fHk = new Font("Consolas", 7.8f, FontStyle.Bold))
            {
                SizeF hkSize = g.MeasureString(hk, fHk);
                Rectangle hkRect = new Rectangle(128, 13, (int)hkSize.Width + 12, 19);
                using (GraphicsPath hkPath = ColorUtils.CreateRoundedRect(hkRect, 6))
                using (SolidBrush hkBg = new SolidBrush(Color.FromArgb(36, 56, 189, 248)))
                using (Pen hkBorder = new Pen(Color.FromArgb(95, 125, 211, 252), 1f))
                using (SolidBrush hkTxt = new SolidBrush(Color.FromArgb(186, 230, 253)))
                {
                    g.FillPath(hkBg, hkPath);
                    g.DrawPath(hkBorder, hkPath);
                    g.DrawString(hk, fHk, hkTxt, hkRect.X + 6, hkRect.Y + 2);
                }
            }

            _rectCollapseBtn = new Rectangle(Width - 94, 10, 24, 24);
            _rectSettingsBtn = new Rectangle(Width - 64, 10, 24, 24);
            _rectHideBtn = new Rectangle(Width - 34, 10, 24, 24);
            DrawHeaderIconBtn(g, _rectCollapseBtn, "\u2013");
            DrawHeaderIconBtn(g, _rectSettingsBtn, "\u2699");
            DrawHeaderIconBtn(g, _rectHideBtn, "\u2715");

            using (Pen sepPen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
            {
                g.DrawLine(sepPen, 16, 38, Width - 16, 38);
            }

            int y = 46;
            Rectangle activeSwatchRect = new Rectangle(16, y, 88, 68);
            Color lighterActive = Color.FromArgb(
                255,
                Math.Min(255, _activeColor.R + 22),
                Math.Min(255, _activeColor.G + 22),
                Math.Min(255, _activeColor.B + 22));

            using (GraphicsPath swPath = ColorUtils.CreateRoundedRect(activeSwatchRect, 6))
            using (LinearGradientBrush swBrush = new LinearGradientBrush(
                Rectangle.Inflate(activeSwatchRect, 1, 1),
                lighterActive,
                _activeColor,
                LinearGradientMode.Vertical))
            using (Pen swBorder = new Pen(Color.FromArgb(165, 255, 255, 255), 1.2f))
            {
                g.FillPath(swBrush, swPath);
                g.DrawPath(swBorder, swPath);
            }

            Color swTextCol = GetContrastTextColor(_activeColor);
            using (Font fSwHex = new Font("Consolas", 9.2f, FontStyle.Bold))
            using (SolidBrush bSwHex = new SolidBrush(swTextCol))
            {
                string actHex = ColorUtils.ToHex(_activeColor);
                SizeF sz = g.MeasureString(actHex, fSwHex);
                g.DrawString(actHex, fSwHex, bSwHex,
                    activeSwatchRect.X + (activeSwatchRect.Width - sz.Width) / 2f,
                    activeSwatchRect.Bottom - 21);
            }

            _rectPickBtn = new Rectangle(114, y, Width - 130, 36);
            bool hoverPick = _rectPickBtn.Contains(_mousePos);
            Color cBtn1 = hoverPick ? Color.FromArgb(215, 56, 189, 248) : Color.FromArgb(180, 99, 102, 241);
            Color cBtn2 = hoverPick ? Color.FromArgb(215, 99, 102, 241) : Color.FromArgb(180, 56, 189, 248);

            using (GraphicsPath btnPath = ColorUtils.CreateRoundedRect(_rectPickBtn, 6))
            using (LinearGradientBrush btnBrush = new LinearGradientBrush(
                Rectangle.Inflate(_rectPickBtn, 1, 1),
                cBtn1,
                cBtn2,
                LinearGradientMode.Horizontal))
            using (Pen btnBorder = new Pen(Color.FromArgb(hoverPick ? 220 : 135, 255, 255, 255), 1.1f))
            using (Font fBtn = new Font("Segoe UI", 9.2f, FontStyle.Bold))
            using (SolidBrush bBtn = new SolidBrush(Color.White))
            {
                g.FillPath(btnBrush, btnPath);
                g.DrawPath(btnBorder, btnPath);
                string btnLabel = "\u2295  Pick Colour (" + _config.HotkeyText + ")";
                SizeF sz = g.MeasureString(btnLabel, fBtn);
                g.DrawString(btnLabel, fBtn, bBtn,
                    _rectPickBtn.X + (_rectPickBtn.Width - sz.Width) / 2f,
                    _rectPickBtn.Y + (_rectPickBtn.Height - sz.Height) / 2f);
            }

            _harmonyRects.Clear();
            List<Color> harmonies = ColorUtils.GetShadesAndHarmonies(_activeColor);
            int harmW = (_rectPickBtn.Width - (harmonies.Count - 1) * 5) / harmonies.Count;
            int harmY = y + 43;
            for (int i = 0; i < harmonies.Count; i++)
            {
                Rectangle hr = new Rectangle(114 + i * (harmW + 5), harmY, harmW, 25);
                _harmonyRects.Add(new KeyValuePair<Rectangle, Color>(hr, harmonies[i]));
                bool hHover = hr.Contains(_mousePos);
                using (GraphicsPath hp = ColorUtils.CreateRoundedRect(hr, 6))
                using (SolidBrush hb = new SolidBrush(harmonies[i]))
                using (Pen hPen = new Pen(hHover ? Color.White : Color.FromArgb(95, 255, 255, 255), hHover ? 1.6f : 1f))
                {
                    g.FillPath(hb, hp);
                    g.DrawPath(hPen, hp);
                }
            }

            y = 125;
            _formatCopyRects.Clear();
            string[] formats = new string[] { "HEX", "RGB", "HSL", "CMYK" };
            int cardW = (Width - 32 - 8) / 2;
            int cardH = 34;
            for (int i = 0; i < formats.Length; i++)
            {
                int col = i % 2;
                int row = i / 2;
                Rectangle cr = new Rectangle(16 + col * (cardW + 8), y + row * (cardH + 6), cardW, cardH);
                string fmt = formats[i];
                string val = ColorUtils.FormatColor(_activeColor, fmt);
                _formatCopyRects[fmt] = cr;
                bool fHover = cr.Contains(_mousePos);

                using (GraphicsPath cp = ColorUtils.CreateRoundedRect(cr, 6))
                using (LinearGradientBrush cb = new LinearGradientBrush(
                    Rectangle.Inflate(cr, 1, 1),
                    fHover ? Color.FromArgb(62, 56, 189, 248) : Color.FromArgb(30, 255, 255, 255),
                    fHover ? Color.FromArgb(28, 99, 102, 241) : Color.FromArgb(14, 255, 255, 255),
                    LinearGradientMode.Vertical))
                using (Pen cPen = new Pen(
                    fHover ? Color.FromArgb(185, 125, 211, 252) : Color.FromArgb(52, 255, 255, 255),
                    1f))
                using (Font fLbl = new Font("Segoe UI", 6.8f, FontStyle.Bold))
                using (Font fVal = new Font("Consolas", 7.8f, FontStyle.Bold))
                using (SolidBrush bLbl = new SolidBrush(fHover ? Color.FromArgb(186, 230, 253) : Color.FromArgb(148, 163, 184)))
                using (SolidBrush bVal = new SolidBrush(Color.FromArgb(245, 248, 255)))
                {
                    g.FillPath(cb, cp);
                    g.DrawPath(cPen, cp);
                    g.DrawString(fmt, fLbl, bLbl, cr.X + 8, cr.Y + 4);
                    g.DrawString(val, fVal, bVal, cr.X + 8, cr.Y + 16);
                }
            }

            y = 214;
            using (Font fSec = new Font("Segoe UI", 7.8f, FontStyle.Bold))
            using (SolidBrush bSec = new SolidBrush(Color.FromArgb(175, 190, 215)))
            {
                g.DrawString(string.Format("RECENT COLOURS ({0}/{1})", _config.History.Count, MAX_HISTORY), fSec, bSec, 16f, y);
            }

            _rectClearHistoryBtn = new Rectangle(Width - 68, y - 2, 52, 20);
            bool hoverClear = _rectClearHistoryBtn.Contains(_mousePos);
            using (Font fClr = new Font("Segoe UI", 7.8f, FontStyle.Regular))
            using (SolidBrush bClr = new SolidBrush(hoverClear ? Color.FromArgb(252, 129, 129) : Color.FromArgb(155, 175, 205)))
            {
                g.DrawString("Clear", fClr, bClr, _rectClearHistoryBtn.X + 12, _rectClearHistoryBtn.Y + 2);
            }

            y += 23;
            _historySwatchRects.Clear();
            _historyPinRects.Clear();

            int cols = 4;
            int gapX = 6;
            int gapY = 6;
            int cellW = (Width - 32 - (cols - 1) * gapX) / cols;
            int cellH = 42;

            for (int i = 0; i < MAX_HISTORY; i++)
            {
                int col = i % cols;
                int row = i / cols;
                Rectangle cellRect = new Rectangle(16 + col * (cellW + gapX), y + row * (cellH + gapY), cellW, cellH);

                if (i < _config.History.Count)
                {
                    ColorItem item = _config.History[i];
                    Color itemCol = item.ToColor();
                    _historySwatchRects.Add(new KeyValuePair<Rectangle, int>(cellRect, i));

                    bool cellHover = cellRect.Contains(_mousePos);
                    bool isSelected = string.Equals(item.Hex, ColorUtils.ToHex(_activeColor), StringComparison.OrdinalIgnoreCase);

                    using (GraphicsPath cellPath = ColorUtils.CreateRoundedRect(cellRect, 6))
                    using (LinearGradientBrush cellBg = new LinearGradientBrush(
                        Rectangle.Inflate(cellRect, 1, 1),
                        cellHover ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(28, 255, 255, 255),
                        cellHover ? Color.FromArgb(32, 56, 189, 248) : Color.FromArgb(14, 255, 255, 255),
                        LinearGradientMode.Vertical))
                    using (Pen cellBorder = new Pen(
                        isSelected
                            ? Color.FromArgb(210, 125, 211, 252)
                            : (cellHover ? Color.FromArgb(175, 255, 255, 255) : Color.FromArgb(52, 255, 255, 255)),
                        isSelected ? 1.5f : 1f))
                    {
                        g.FillPath(cellBg, cellPath);
                        g.DrawPath(cellBorder, cellPath);
                    }

                    Rectangle colorBarRect = new Rectangle(cellRect.X + 4, cellRect.Y + 4, cellRect.Width - 8, 18);
                    using (GraphicsPath barPath = ColorUtils.CreateRoundedRect(colorBarRect, 4))
                    using (SolidBrush barBrush = new SolidBrush(itemCol))
                    using (Pen barPen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                    {
                        g.FillPath(barBrush, barPath);
                        g.DrawPath(barPen, barPath);
                    }

                    // Generous 18x18 pin target in top-right corner of the swatch bar
                    Rectangle pinHitRect = new Rectangle(colorBarRect.Right - 17, colorBarRect.Y, 17, 17);
                    _historyPinRects.Add(new KeyValuePair<Rectangle, int>(pinHitRect, i));

                    if (item.Pinned || cellHover)
                    {
                        bool pinHover = pinHitRect.Contains(_mousePos);
                        using (Font fStar = new Font("Segoe UI Symbol", 7.2f, FontStyle.Bold))
                        using (SolidBrush bStar = new SolidBrush(
                            item.Pinned
                                ? Color.Gold
                                : (pinHover ? Color.Gold : Color.FromArgb(185, GetContrastTextColor(itemCol)))))
                        {
                            g.DrawString("\u2605", fStar, bStar, colorBarRect.Right - 14, colorBarRect.Y);
                        }
                    }

                    using (Font fItemHex = new Font("Consolas", 7.5f, FontStyle.Bold))
                    using (SolidBrush bItemHex = new SolidBrush(Color.FromArgb(235, 242, 255)))
                    {
                        SizeF sz = g.MeasureString(item.Hex, fItemHex);
                        g.DrawString(item.Hex, fItemHex, bItemHex,
                            cellRect.X + (cellRect.Width - sz.Width) / 2f,
                            cellRect.Y + 24);
                    }
                }
                else
                {
                    using (GraphicsPath emptyPath = ColorUtils.CreateRoundedRect(cellRect, 6))
                    using (SolidBrush emptyBg = new SolidBrush(Color.FromArgb(14, 255, 255, 255)))
                    using (Pen emptyPen = new Pen(Color.FromArgb(28, 255, 255, 255), 1f))
                    {
                        g.FillPath(emptyBg, emptyPath);
                        g.DrawPath(emptyPen, emptyPath);
                    }
                }
            }

            int footerY = Height - 26;
            if (!string.IsNullOrEmpty(_toastMessage))
            {
                Rectangle toastRect = new Rectangle(16, footerY - 2, Width - 32, 22);
                using (GraphicsPath tp = ColorUtils.CreateRoundedRect(toastRect, 6))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(48, 56, 189, 248)))
                using (Pen tPen = new Pen(Color.FromArgb(110, 125, 211, 252), 1f))
                using (Font fToast = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (SolidBrush bToast = new SolidBrush(Color.FromArgb(186, 230, 253)))
                {
                    g.FillPath(tb, tp);
                    g.DrawPath(tPen, tp);
                    SizeF sz = g.MeasureString(_toastMessage, fToast);
                    g.DrawString(_toastMessage, fToast, bToast,
                        toastRect.X + (toastRect.Width - sz.Width) / 2f,
                        toastRect.Y + 3);
                }
            }
            else
            {
                using (Font fFoot = new Font("Segoe UI", 7.3f, FontStyle.Regular))
                using (SolidBrush bFoot = new SolidBrush(Color.FromArgb(145, 165, 195)))
                {
                    string hint = "Click any swatch to copy HEX • Right-click to copy RGB • \u2605 to Pin";
                    SizeF sz = g.MeasureString(hint, fFoot);
                    g.DrawString(hint, fFoot, bFoot, (Width - sz.Width) / 2f, footerY);
                }
            }
        }

        private void DrawHeaderIconBtn(Graphics g, Rectangle r, string symbol)
        {
            bool hover = r.Contains(_mousePos);
            if (hover)
            {
                using (GraphicsPath gp = ColorUtils.CreateRoundedRect(r, 6))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                {
                    g.FillPath(hb, gp);
                }
            }
            using (Font f = new Font("Segoe UI Symbol", 8.8f, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(hover ? Color.White : Color.FromArgb(170, 180, 205)))
            {
                SizeF sz = g.MeasureString(symbol, f);
                g.DrawString(symbol, f, b, r.X + (r.Width - sz.Width) / 2f, r.Y + (r.Height - sz.Height) / 2f);
            }
        }

        private Color GetContrastTextColor(Color bg)
        {
            double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return (lum > 0.58) ? Color.FromArgb(20, 24, 35) : Color.White;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && !IsInteractivePoint(e.Location))
            {
                _isDragging = true;
                _hasMovedDuringDrag = false;
                _dragStartMouse = Cursor.Position;
                _dragStartForm = Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mousePos = e.Location;
            if (_isDragging)
            {
                Point cur = Cursor.Position;
                int dx = cur.X - _dragStartMouse.X;
                int dy = cur.Y - _dragStartMouse.Y;
                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    _hasMovedDuringDrag = true;
                if (_hasMovedDuringDrag)
                    Location = new Point(_dragStartForm.X + dx, _dragStartForm.Y + dy);
            }
            else
            {
                Cursor = IsInteractivePoint(e.Location) ? Cursors.Hand : Cursors.SizeAll;
                UpdateHoverTooltip(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                if (_hasMovedDuringDrag)
                {
                    if (_config.SnapToEdges)
                        SnapToNearestScreenEdge();
                    SavePositionAndState();
                    return;
                }
            }
            HandleMouseClickAction(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mousePos = new Point(-1, -1);
            Invalidate();
        }

        private void SnapToNearestScreenEdge()
        {
            Screen scr = Screen.FromControl(this);
            Rectangle wa = scr.WorkingArea;
            int snapDist = 22;
            int x = Left;
            int y = Top;

            if (Math.Abs(x - wa.Left) < snapDist) x = wa.Left + 6;
            else if (Math.Abs((x + Width) - wa.Right) < snapDist) x = wa.Right - Width - 6;

            if (Math.Abs(y - wa.Top) < snapDist) y = wa.Top + 6;
            else if (Math.Abs((y + Height) - wa.Bottom) < snapDist) y = wa.Bottom - Height - 6;

            Location = new Point(x, y);
        }

        private bool IsInteractivePoint(Point pt)
        {
            if (_isCollapsed)
                return _rectMiniPickCircle.Contains(pt);

            if (_rectPickBtn.Contains(pt) ||
                _rectCollapseBtn.Contains(pt) ||
                _rectSettingsBtn.Contains(pt) ||
                _rectHideBtn.Contains(pt) ||
                _rectClearHistoryBtn.Contains(pt))
                return true;

            foreach (KeyValuePair<string, Rectangle> kv in _formatCopyRects)
            {
                if (kv.Value.Contains(pt)) return true;
            }
            foreach (KeyValuePair<Rectangle, Color> kv in _harmonyRects)
            {
                if (kv.Key.Contains(pt)) return true;
            }
            foreach (KeyValuePair<Rectangle, int> kv in _historyPinRects)
            {
                if (kv.Key.Contains(pt)) return true;
            }
            foreach (KeyValuePair<Rectangle, int> kv in _historySwatchRects)
            {
                if (kv.Key.Contains(pt)) return true;
            }
            return false;
        }

        private void UpdateHoverTooltip(Point pt)
        {
            string tip = "";
            if (_isCollapsed)
            {
                if (_rectMiniPickCircle.Contains(pt))
                    tip = "Click to Pick Colour (" + _config.HotkeyText + ")";
                else if (_rectMiniExpandArea.Contains(pt))
                    tip = "Click to Expand Widget | Drag to Move | Right-click for Menu";
            }
            else
            {
                if (_rectCollapseBtn.Contains(pt))
                    tip = "Collapse to Floating Mini Icon";
                else if (_rectSettingsBtn.Contains(pt))
                    tip = "Shortcut & Widget Settings";
                else if (_rectHideBtn.Contains(pt))
                    tip = "Hide Widget to System Tray (" + _config.HotkeyText + " still works!)";
                else if (_rectPickBtn.Contains(pt))
                    tip = "Pick any pixel from screen (" + _config.HotkeyText + ")";
                else if (_rectClearHistoryBtn.Contains(pt))
                    tip = "Clear unpinned colours from history";
                else
                {
                    foreach (KeyValuePair<Rectangle, Color> kv in _harmonyRects)
                    {
                        if (kv.Key.Contains(pt))
                        {
                            tip = "Click to select & copy shade " + ColorUtils.ToHex(kv.Value);
                            break;
                        }
                    }
                    if (string.IsNullOrEmpty(tip))
                    {
                        foreach (KeyValuePair<Rectangle, int> kv in _historyPinRects)
                        {
                            if (kv.Key.Contains(pt) && kv.Value < _config.History.Count)
                            {
                                tip = _config.History[kv.Value].Pinned ? "Click ★ to Unpin colour" : "Click ★ to Pin / Lock colour";
                                break;
                            }
                        }
                    }
                }
            }

            if (tip != _lastTooltipText)
            {
                _lastTooltipText = tip;
                _toolTip.SetToolTip(this, tip);
            }
        }

        private void HandleMouseClickAction(MouseEventArgs e)
        {
            Point pt = e.Location;
            if (e.Button == MouseButtons.Right && _isCollapsed)
            {
                _trayMenu.Show(Cursor.Position);
                return;
            }

            if (_isCollapsed)
            {
                if (_rectMiniPickCircle.Contains(pt))
                    TriggerScreenPicker();
                else if (_rectMiniExpandArea.Contains(pt))
                    ShowAndRestoreWidget(false);
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (_rectPickBtn.Contains(pt))
                {
                    TriggerScreenPicker();
                    return;
                }
                if (_rectCollapseBtn.Contains(pt))
                {
                    ShowAndRestoreWidget(true);
                    return;
                }
                if (_rectSettingsBtn.Contains(pt))
                {
                    OpenSettings();
                    return;
                }
                if (_rectHideBtn.Contains(pt))
                {
                    SavePositionAndState();
                    Visible = false;
                    _trayIcon.ShowBalloonTip(1500, "Colour Picker Hidden", "Press " + _config.HotkeyText + " anytime to pick a colour!", ToolTipIcon.Info);
                    return;
                }
                if (_rectClearHistoryBtn.Contains(pt))
                {
                    _config.History.RemoveAll(x => !x.Pinned);
                    _config.Save();
                    ShowToast("Cleared unpinned history");
                    Invalidate();
                    return;
                }

                foreach (KeyValuePair<string, Rectangle> kv in _formatCopyRects)
                {
                    if (kv.Value.Contains(pt))
                    {
                        string code = ColorUtils.FormatColor(_activeColor, kv.Key);
                        CopyToClipboard(code);
                        return;
                    }
                }

                foreach (KeyValuePair<Rectangle, Color> kv in _harmonyRects)
                {
                    if (kv.Key.Contains(pt))
                    {
                        _activeColor = kv.Value;
                        string hex = ColorUtils.ToHex(_activeColor);
                        CopyToClipboard(hex, "Shade Copied " + hex);
                        UpdateTrayIcon();
                        Invalidate();
                        return;
                    }
                }

                foreach (KeyValuePair<Rectangle, int> kv in _historyPinRects)
                {
                    if (kv.Key.Contains(pt) && kv.Value < _config.History.Count)
                    {
                        _config.History[kv.Value].Pinned = !_config.History[kv.Value].Pinned;
                        _config.Save();
                        ShowToast(_config.History[kv.Value].Pinned ? "Pinned colour" : "Unpinned colour");
                        Invalidate();
                        return;
                    }
                }
            }

            foreach (KeyValuePair<Rectangle, int> kv in _historySwatchRects)
            {
                if (kv.Key.Contains(pt) && kv.Value < _config.History.Count)
                {
                    ColorItem item = _config.History[kv.Value];
                    _activeColor = item.ToColor();
                    UpdateTrayIcon();
                    if (e.Button == MouseButtons.Right)
                        CopyToClipboard(ColorUtils.ToRgbString(_activeColor));
                    else
                        CopyToClipboard(item.Hex);
                    Invalidate();
                    return;
                }
            }
        }
    }

    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew = false;
            try
            {
                _singleInstanceMutex = new Mutex(true, "Local\\ColourPickerWidget_SingleInstance_Mutex", out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Wake up the already running instance instead of spawning a duplicate
                if (NativeMethods.WM_SHOWEXISTING != 0)
                {
                    NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SHOWEXISTING, IntPtr.Zero, IntPtr.Zero);
                }
                return;
            }

            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWidgetForm());

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
            }
        }
    }
}
