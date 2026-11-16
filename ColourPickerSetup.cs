using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Colour Picker Widget Setup")]
[assembly: AssemblyDescription("Setup & Uninstaller Wizard for Colour Picker Widget")]
[assembly: AssemblyCompany("Custom Desktop Widgets")]
[assembly: AssemblyProduct("Colour Picker Widget")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace ColourPickerInstaller
{
    public static class NativeDwm
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void ApplyDarkWindow(IntPtr hwnd)
        {
            try
            {
                int dark = 1;
                DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                int roundPref = 2;
                DwmSetWindowAttribute(hwnd, 33, ref roundPref, 4);
                int borderCol = 0x00A5734B;
                DwmSetWindowAttribute(hwnd, 34, ref borderCol, 4);
            }
            catch { }
        }
    }

    public class InstallerForm : Form
    {
        private const string APP_NAME = "Colour Picker Widget";
        private const string APP_VERSION = "1.2.1";
        private const string UNINSTALL_REG_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ColourPickerWidget";
        private const string STARTUP_REG_KEY = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string STARTUP_VAL_NAME = "ColourPickerWidget";

        private TextBox _txtInstallPath;
        private CheckBox _chkDesktopShortcut;
        private CheckBox _chkStartMenuShortcut;
        private CheckBox _chkRunAtStartup;
        private CheckBox _chkLaunchNow;
        private Button _btnInstall;
        private Button _btnUninstall;
        private Label _lblStatus;
        private Icon _appIcon;

        public InstallerForm()
        {
            Text = "Colour Picker Widget \u2014 Setup Wizard";
            Size = new Size(500, 445);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            BackColor = Color.FromArgb(16, 19, 28);
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 9.2f, FontStyle.Regular);
            DoubleBuffered = true;

            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream("app.ico"))
                {
                    if (s != null)
                    {
                        _appIcon = new Icon(s, new Size(48, 48));
                        Icon = _appIcon;
                    }
                }
                if (_appIcon == null)
                {
                    _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    Icon = _appIcon;
                }
            }
            catch { }

            string defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs\\ColourPickerWidget");

            int y = 92;
            Label lblDir = new Label
            {
                Text = "Installation Folder:",
                Location = new Point(26, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 210, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            Controls.Add(lblDir);

            y += 24;
            _txtInstallPath = new TextBox
            {
                Text = defaultDir,
                Location = new Point(26, y),
                Size = new Size(345, 27),
                BackColor = Color.FromArgb(28, 33, 48),
                ForeColor = Color.FromArgb(235, 242, 255),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.2f)
            };
            Controls.Add(_txtInstallPath);

            Button btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(379, y - 1),
                Size = new Size(80, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(40, 47, 68),
                ForeColor = Color.White
            };
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(80, 125, 211, 252);
            btnBrowse.Click += (s, e) =>
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "Select folder to install Colour Picker Widget:";
                    fbd.SelectedPath = _txtInstallPath.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        _txtInstallPath.Text = Path.Combine(fbd.SelectedPath, "ColourPickerWidget");
                    }
                }
            };
            Controls.Add(btnBrowse);

            y += 42;
            Label lblOpts = new Label
            {
                Text = "Setup Options:",
                Location = new Point(26, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(190, 210, 235),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            Controls.Add(lblOpts);

            y += 26;
            _chkDesktopShortcut = new CheckBox
            {
                Text = "Create a Desktop shortcut",
                Location = new Point(30, y),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(230, 238, 250)
            };
            Controls.Add(_chkDesktopShortcut);

            y += 28;
            _chkStartMenuShortcut = new CheckBox
            {
                Text = "Create a Start Menu shortcut",
                Location = new Point(30, y),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(230, 238, 250)
            };
            Controls.Add(_chkStartMenuShortcut);

            y += 28;
            _chkRunAtStartup = new CheckBox
            {
                Text = "Launch automatically when Windows starts",
                Location = new Point(30, y),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(230, 238, 250)
            };
            Controls.Add(_chkRunAtStartup);

            y += 28;
            _chkLaunchNow = new CheckBox
            {
                Text = "Launch Colour Picker Widget immediately after installation",
                Location = new Point(30, y),
                AutoSize = true,
                Checked = true,
                ForeColor = Color.FromArgb(125, 211, 252)
            };
            Controls.Add(_chkLaunchNow);

            y += 38;
            _lblStatus = new Label
            {
                Text = "Ready to install on this PC (Windows 10 / 11 compatible).",
                Location = new Point(26, y),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            Controls.Add(_lblStatus);

            y += 30;
            _btnInstall = new Button
            {
                Text = "Install Now",
                Location = new Point(26, y),
                Size = new Size(195, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 189, 248),
                ForeColor = Color.FromArgb(12, 18, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += BtnInstall_Click;
            Controls.Add(_btnInstall);

            _btnUninstall = new Button
            {
                Text = "Uninstall Existing",
                Location = new Point(232, y),
                Size = new Size(130, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 39, 54),
                ForeColor = Color.FromArgb(248, 113, 113),
                Font = new Font("Segoe UI", 8.8f, FontStyle.Regular),
                Cursor = Cursors.Hand
            };
            _btnUninstall.FlatAppearance.BorderColor = Color.FromArgb(90, 248, 113, 113);
            _btnUninstall.Click += BtnUninstall_Click;
            Controls.Add(_btnUninstall);

            Button btnClose = new Button
            {
                Text = "Close",
                Location = new Point(372, y),
                Size = new Size(88, 38),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(35, 39, 54),
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeDwm.ApplyDarkWindow(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Rectangle headerRect = new Rectangle(0, 0, Width, 78);
            using (LinearGradientBrush bg = new LinearGradientBrush(
                headerRect,
                Color.FromArgb(24, 32, 52),
                Color.FromArgb(16, 19, 28),
                LinearGradientMode.Vertical))
            {
                g.FillRectangle(bg, headerRect);
            }
            using (Pen sep = new Pen(Color.FromArgb(40, 125, 211, 252), 1f))
            {
                g.DrawLine(sep, 0, 77, Width, 77);
            }

            if (_appIcon != null)
            {
                using (Bitmap bmp = _appIcon.ToBitmap())
                {
                    g.DrawImage(bmp, new Rectangle(24, 15, 48, 48));
                }
            }

            using (Font fTitle = new Font("Segoe UI", 13f, FontStyle.Bold))
            using (Font fSub = new Font("Segoe UI", 8.5f, FontStyle.Regular))
            using (SolidBrush bTitle = new SolidBrush(Color.FromArgb(245, 248, 255)))
            using (SolidBrush bSub = new SolidBrush(Color.FromArgb(125, 211, 252)))
            {
                g.DrawString("Colour Picker Widget", fTitle, bTitle, 84f, 16f);
                g.DrawString("v" + APP_VERSION + " \u2022 Glassmorphic Screen Eyedropper & Floating Widget", fSub, bSub, 86f, 44f);
            }
        }

        private void BtnInstall_Click(object sender, EventArgs e)
        {
            try
            {
                string installDir = _txtInstallPath.Text.Trim();
                if (string.IsNullOrEmpty(installDir))
                {
                    MessageBox.Show("Please specify a valid installation folder.", "Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _lblStatus.Text = "Stopping any running widget instances...";
                _lblStatus.ForeColor = Color.FromArgb(125, 211, 252);
                Application.DoEvents();
                StopRunningInstances();

                if (!Directory.Exists(installDir))
                    Directory.CreateDirectory(installDir);

                string exePath = Path.Combine(installDir, "ColourPickerWidget.exe");
                string icoPath = Path.Combine(installDir, "app.ico");
                string readmePath = Path.Combine(installDir, "README.md");
                string uninstallPath = Path.Combine(installDir, "Uninstall.exe");

                _lblStatus.Text = "Extracting application files...";
                Application.DoEvents();

                ExtractResource("ColourPickerWidget.exe", exePath);
                ExtractResource("app.ico", icoPath);
                ExtractResource("README.md", readmePath);

                string selfPath = Application.ExecutablePath;
                if (!string.Equals(Path.GetFullPath(selfPath), Path.GetFullPath(uninstallPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(selfPath, uninstallPath, true);
                }

                if (_chkDesktopShortcut.Checked)
                {
                    string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Colour Picker Widget.lnk");
                    CreateShortcut(deskLnk, exePath, installDir, icoPath, "Launch Colour Picker Widget (Alt + C)", "");
                }

                if (_chkStartMenuShortcut.Checked)
                {
                    string smDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Colour Picker Widget");
                    if (!Directory.Exists(smDir))
                        Directory.CreateDirectory(smDir);

                    CreateShortcut(
                        Path.Combine(smDir, "Colour Picker Widget.lnk"),
                        exePath,
                        installDir,
                        icoPath,
                        "Launch Colour Picker Widget (Alt + C)",
                        "");
                    CreateShortcut(
                        Path.Combine(smDir, "Uninstall Colour Picker Widget.lnk"),
                        uninstallPath,
                        installDir,
                        icoPath,
                        "Uninstall Colour Picker Widget",
                        "/uninstall");
                }

                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, true))
                {
                    if (rk != null)
                    {
                        if (_chkRunAtStartup.Checked)
                            rk.SetValue(STARTUP_VAL_NAME, "\"" + exePath + "\"");
                        else if (rk.GetValue(STARTUP_VAL_NAME) != null)
                            rk.DeleteValue(STARTUP_VAL_NAME, false);
                    }
                }

                using (RegistryKey uk = Registry.CurrentUser.CreateSubKey(UNINSTALL_REG_KEY))
                {
                    if (uk != null)
                    {
                        uk.SetValue("DisplayName", APP_NAME);
                        uk.SetValue("DisplayVersion", APP_VERSION);
                        uk.SetValue("Publisher", "Custom Desktop Widgets");
                        uk.SetValue("DisplayIcon", icoPath);
                        uk.SetValue("InstallLocation", installDir);
                        uk.SetValue("UninstallString", "\"" + uninstallPath + "\" /uninstall");
                        uk.SetValue("NoModify", 1, RegistryValueKind.DWord);
                        uk.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    }
                }

                try
                {
                    NativeDwm.SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }

                _lblStatus.Text = "\u2713 Installation completed successfully!";
                _lblStatus.ForeColor = Color.FromArgb(110, 231, 183);

                if (_chkLaunchNow.Checked)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(exePath)
                    {
                        WorkingDirectory = installDir,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }

                MessageBox.Show(
                    "Colour Picker Widget has been installed successfully!\n\n\u2022 Location: " + installDir + "\n\u2022 Global Shortcut: Alt + C (Customizable in Settings)",
                    "Installation Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Installation error: " + ex.Message;
                _lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                MessageBox.Show("Failed to install:\n" + ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnUninstall_Click(object sender, EventArgs e)
        {
            PerformUninstall(this, _txtInstallPath.Text.Trim());
        }

        public static void PerformUninstall(Form owner, string fallbackDir)
        {
            DialogResult dr = MessageBox.Show(
                owner,
                "Are you sure you want to uninstall Colour Picker Widget from your computer?",
                "Uninstall Colour Picker Widget",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (dr != DialogResult.Yes)
                return;

            try
            {
                StopRunningInstances();
                string installDir = fallbackDir;
                using (RegistryKey uk = Registry.CurrentUser.OpenSubKey(UNINSTALL_REG_KEY, false))
                {
                    if (uk != null && uk.GetValue("InstallLocation") != null)
                        installDir = uk.GetValue("InstallLocation").ToString();
                }

                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(STARTUP_REG_KEY, true))
                {
                    if (rk != null && rk.GetValue(STARTUP_VAL_NAME) != null)
                        rk.DeleteValue(STARTUP_VAL_NAME, false);
                }

                Registry.CurrentUser.DeleteSubKeyTree(UNINSTALL_REG_KEY, false);

                string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Colour Picker Widget.lnk");
                if (File.Exists(deskLnk)) File.Delete(deskLnk);

                string smDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Colour Picker Widget");
                if (Directory.Exists(smDir)) Directory.Delete(smDir, true);

                if (!string.IsNullOrEmpty(installDir) && Directory.Exists(installDir))
                {
                    foreach (string fName in new string[] { "ColourPickerWidget.exe", "app.ico", "README.md", "widget_data.json", "widget_data.json.tmp" })
                    {
                        string fp = Path.Combine(installDir, fName);
                        if (File.Exists(fp))
                        {
                            try { File.Delete(fp); } catch { }
                        }
                    }

                    string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
                    string selfPath = Path.GetFullPath(Application.ExecutablePath);
                    if (File.Exists(uninstallExe))
                    {
                        if (!string.Equals(selfPath, Path.GetFullPath(uninstallExe), StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(uninstallExe); } catch { }
                            try { Directory.Delete(installDir, false); } catch { }
                        }
                        else
                        {
                            // Schedule self-deletion after process exits
                            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe",
                                "/C timeout /t 1 /nobreak >nul & del /f /q \"" + uninstallExe + "\" & rmdir \"" + installDir + "\"")
                            {
                                WindowStyle = ProcessWindowStyle.Hidden,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            try { Process.Start(psi); } catch { }
                        }
                    }
                }

                MessageBox.Show(owner, "Colour Picker Widget has been uninstalled.", "Uninstall Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (owner != null) owner.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Uninstall error:\n" + ex.Message, "Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void StopRunningInstances()
        {
            string[] names = new string[] { "ColourPickerWidget", "ColourPickerWidget_Portable" };
            foreach (string procName in names)
            {
                try
                {
                    Process[] procs = Process.GetProcessesByName(procName);
                    foreach (Process p in procs)
                    {
                        try
                        {
                            if (p.CloseMainWindow())
                            {
                                if (p.WaitForExit(800))
                                    continue;
                            }
                            p.Kill();
                            p.WaitForExit(1500);
                        }
                        catch { }
                        finally
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        private static void ExtractResource(string resourceName, string destPath)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resourceName))
            {
                if (s == null)
                    throw new Exception("Embedded resource missing: " + resourceName);
                using (FileStream fs = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                {
                    s.CopyTo(fs);
                }
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetExe, string workDir, string iconPath, string description, string arguments = "")
        {
            object shell = null;
            object shortcut = null;
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return;
                shell = Activator.CreateInstance(t);
                shortcut = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type st = shortcut.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workDir });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { iconPath + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                if (!string.IsNullOrEmpty(arguments))
                    st.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut))
                {
                    try { Marshal.FinalReleaseComObject(shortcut); } catch { }
                }
                if (shell != null && Marshal.IsComObject(shell))
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                NativeDwm.SetProcessDPIAware();
            }
            catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length > 0)
            {
                string a = args[0].ToLowerInvariant();
                if (a == "/uninstall" || a == "--uninstall" || a == "-u")
                {
                    InstallerForm.PerformUninstall(null, AppDomain.CurrentDomain.BaseDirectory);
                    return;
                }
            }

            Application.Run(new InstallerForm());
        }
    }
}
