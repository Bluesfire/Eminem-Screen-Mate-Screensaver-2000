using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Eminem ScreenMate Control")]
[assembly: AssemblyDescription("Control panel for the recovered 2000 Eminem ScreenMate compatibility package")]
[assembly: AssemblyCompany("Preservation compatibility package")]
[assembly: AssemblyProduct("Eminem ScreenMate")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

internal static class ControlProgram
{
    private static readonly string ScreenSaverPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        @"System32\Eminem ScreenMate.scr"
    );

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ControlForm());
    }

    private sealed class ControlForm : Form
    {
        private readonly Label status;

        public ControlForm()
        {
            Text = "Eminem ScreenMate";
            ClientSize = new Size(460, 275);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.0f);

            Label title = new Label();
            title.Text = "Eminem ScreenMate";
            title.Font = new Font("Segoe UI", 18.0f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(22, 18);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Original 2000 desktop ScreenMate · Windows 10/11 compatibility edition";
            subtitle.AutoSize = true;
            subtitle.ForeColor = SystemColors.GrayText;
            subtitle.Location = new Point(25, 58);
            Controls.Add(subtitle);

            status = new Label();
            status.AutoSize = false;
            status.Size = new Size(410, 28);
            status.Location = new Point(25, 90);
            status.Font = new Font("Segoe UI", 10.0f, FontStyle.Bold);
            Controls.Add(status);

            Button enable = MakeButton("Enable", 25, 132);
            enable.Click += delegate { EnableSaver(); RefreshStatus(); };
            Controls.Add(enable);

            Button disable = MakeButton("Disable", 145, 132);
            disable.Click += delegate { DisableSaver(); RefreshStatus(); };
            Controls.Add(disable);

            Button run = MakeButton("Run now", 265, 132);
            run.Click += delegate { RunSaver(); };
            Controls.Add(run);

            Button settings = new Button();
            settings.Text = "Open Windows Screen Saver Settings";
            settings.Size = new Size(360, 34);
            settings.Location = new Point(25, 180);
            settings.Click += delegate { OpenSettings(); };
            Controls.Add(settings);

            Button close = new Button();
            close.Text = "Close";
            close.Size = new Size(75, 28);
            close.Location = new Point(360, 232);
            close.Click += delegate { Close(); };
            Controls.Add(close);

            RefreshStatus();
        }

        private static Button MakeButton(string text, int x, int y)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(110, 34);
            button.Location = new Point(x, y);
            return button;
        }

        private void RefreshStatus()
        {
            bool selected = false;
            bool active = false;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
                {
                    if (key != null)
                    {
                        string current = key.GetValue("SCRNSAVE.EXE") as string;
                        string activeValue = key.GetValue("ScreenSaveActive") as string;
                        selected = PathsEqual(current, ScreenSaverPath);
                        active = String.Equals(activeValue, "1", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch { }

            status.Text = selected && active
                ? "Status: Enabled as the current Windows screen saver"
                : "Status: Disabled";
            status.ForeColor = selected && active ? Color.DarkGreen : Color.DarkRed;
        }

        private static void EnableSaver()
        {
            if (!File.Exists(ScreenSaverPath))
            {
                MessageBox.Show("The installed screen saver wrapper is missing.", "Eminem ScreenMate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            {
                key.SetValue("SCRNSAVE.EXE", ScreenSaverPath, RegistryValueKind.String);
                key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
            }
        }

        private static void DisableSaver()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            {
                string current = key.GetValue("SCRNSAVE.EXE") as string;
                if (PathsEqual(current, ScreenSaverPath))
                    key.DeleteValue("SCRNSAVE.EXE", false);
                key.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
            }
        }

        private static void RunSaver()
        {
            if (!File.Exists(ScreenSaverPath))
            {
                MessageBox.Show("The installed screen saver wrapper is missing.", "Eminem ScreenMate", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Process.Start(new ProcessStartInfo(ScreenSaverPath, "/s") { UseShellExecute = false });
        }

        private static void OpenSettings()
        {
            Process.Start(new ProcessStartInfo("control.exe", "desk.cpl,,@screensaver") { UseShellExecute = true });
        }

        private static bool PathsEqual(string a, string b)
        {
            if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b))
                return false;
            try
            {
                return String.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return String.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
