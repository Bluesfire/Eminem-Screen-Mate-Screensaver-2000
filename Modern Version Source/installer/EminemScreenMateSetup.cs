using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Eminem ScreenMate Setup")]
[assembly: AssemblyDescription("Installer for the recovered 2000 Eminem ScreenMate compatibility package")]
[assembly: AssemblyCompany("Preservation compatibility package")]
[assembly: AssemblyProduct("Eminem ScreenMate")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

internal static class SetupProgram
{
    private const string ProductName = "Eminem ScreenMate";
    private const string Version = "1.3.0";
    private const string PayloadResource = "EminemScreenMate.Payload.zip";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }

    private sealed class SetupForm : Form
    {
        private readonly CheckBox enableNow;
        private readonly CheckBox launchControl;
        private readonly Button installButton;
        private readonly Button cancelButton;
        private readonly ProgressBar progress;
        private readonly Label status;

        public SetupForm()
        {
            Text = "Eminem ScreenMate Setup";
            ClientSize = new Size(520, 335);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.0f);

            Label title = new Label();
            title.Text = "Eminem ScreenMate";
            title.Font = new Font("Segoe UI", 20.0f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(28, 24);
            Controls.Add(title);

            Label intro = new Label();
            intro.Text = "Install the recovered 2000 ScreenMate for 64-bit Windows 10 and 11.";
            intro.AutoSize = true;
            intro.Location = new Point(31, 70);
            Controls.Add(intro);

            Label details = new Label();
            details.Text = "Setup installs a private WineVDM compatibility runtime under Program Files\n" +
                           "and a modern 64-bit screen-saver wrapper in Windows System32.\n" +
                           "The original Eminem animation and media files are preserved unchanged.";
            details.AutoSize = true;
            details.ForeColor = SystemColors.GrayText;
            details.Location = new Point(31, 101);
            Controls.Add(details);

            enableNow = new CheckBox();
            enableNow.Text = "Set Eminem ScreenMate as my current screen saver";
            enableNow.Checked = true;
            enableNow.AutoSize = true;
            enableNow.Location = new Point(34, 176);
            Controls.Add(enableNow);

            launchControl = new CheckBox();
            launchControl.Text = "Open the Eminem ScreenMate control panel after setup";
            launchControl.Checked = true;
            launchControl.AutoSize = true;
            launchControl.Location = new Point(34, 203);
            Controls.Add(launchControl);

            status = new Label();
            status.Text = "Ready to install.";
            status.AutoSize = true;
            status.Location = new Point(31, 244);
            Controls.Add(status);

            progress = new ProgressBar();
            progress.Size = new Size(455, 18);
            progress.Location = new Point(32, 266);
            progress.Style = ProgressBarStyle.Continuous;
            Controls.Add(progress);

            installButton = new Button();
            installButton.Text = "Install";
            installButton.Size = new Size(90, 30);
            installButton.Location = new Point(300, 296);
            installButton.Click += InstallClicked;
            Controls.Add(installButton);

            cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Size = new Size(90, 30);
            cancelButton.Location = new Point(397, 296);
            cancelButton.Click += delegate { Close(); };
            Controls.Add(cancelButton);

            AcceptButton = installButton;
            CancelButton = cancelButton;
        }

        private void InstallClicked(object sender, EventArgs e)
        {
            installButton.Enabled = false;
            cancelButton.Enabled = false;
            enableNow.Enabled = false;
            launchControl.Enabled = false;
            UseWaitCursor = true;

            try
            {
                InstallProduct(enableNow.Checked);
                progress.Value = 100;
                status.Text = "Installation complete.";
                UseWaitCursor = false;

                MessageBox.Show(
                    "Eminem ScreenMate is installed and available in Windows Screen Saver Settings.",
                    SetupProgram.ProductName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );

                if (launchControl.Checked)
                {
                    string control = Path.Combine(GetInstallDir(), "Eminem ScreenMate Control.exe");
                    if (File.Exists(control))
                    {
                        try
                        {
                            LaunchControlUnelevated(control);
                        }
                        catch (Exception launchEx)
                        {
                            MessageBox.Show(
                                "Installation completed, but the control panel could not be opened automatically.\n\n" +
                                "Open Eminem ScreenMate from the Start Menu instead.\n\n" + launchEx.Message,
                                SetupProgram.ProductName,
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                        }
                    }
                }

                Close();
            }
            catch (Exception ex)
            {
                UseWaitCursor = false;
                installButton.Enabled = true;
                cancelButton.Enabled = true;
                enableNow.Enabled = true;
                launchControl.Enabled = true;
                status.Text = "Installation failed.";
                MessageBox.Show("Setup could not complete.\n\n" + ex.Message, SetupProgram.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InstallProduct(bool selectSaver)
        {
            string installDir = GetInstallDir();
            string staging = Path.Combine(Path.GetTempPath(), "EminemScreenMateSetup_" + Guid.NewGuid().ToString("N"));

            status.Text = "Extracting files...";
            progress.Value = 10;
            Application.DoEvents();

            Directory.CreateDirectory(staging);
            try
            {
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
                {
                    if (payload == null)
                        throw new InvalidOperationException("The embedded installation payload is missing.");

                    using (ZipArchive zip = new ZipArchive(payload, ZipArchiveMode.Read))
                    {
                        int completed = 0;
                        foreach (ZipArchiveEntry entry in zip.Entries)
                        {
                            string target = SafeCombine(staging, entry.FullName);
                            if (String.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(target);
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                using (Stream input = entry.Open())
                                using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                                    input.CopyTo(output);
                            }

                            completed++;
                            progress.Value = 10 + (int)(45.0 * completed / Math.Max(1, zip.Entries.Count));
                            if ((completed & 7) == 0) Application.DoEvents();
                        }
                    }
                }

                status.Text = "Installing compatibility runtime...";
                progress.Value = 60;
                Application.DoEvents();

                CopyTree(staging, installDir);

                string wrapper = Path.Combine(installDir, "Eminem ScreenMate.scr");
                string systemScr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Eminem ScreenMate.scr");
                File.Copy(wrapper, systemScr, true);

                status.Text = "Registering with Windows...";
                progress.Value = 78;
                Application.DoEvents();

                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"Software\EminemScreenMate"))
                {
                    key.SetValue("InstallDir", installDir, RegistryValueKind.String);
                    key.SetValue("Version", SetupProgram.Version, RegistryValueKind.String);
                }

                // Remove the per-user key used by the earlier portable prototype.
                // The installed System32 wrapper should resolve the Program Files copy.
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\EminemScreenMate", false); } catch { }

                RegisterUninstall(installDir);
                CreateStartMenuShortcuts(installDir);

                if (selectSaver)
                {
                    using (RegistryKey desktop = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
                    {
                        desktop.SetValue("SCRNSAVE.EXE", systemScr, RegistryValueKind.String);
                        desktop.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                    }
                }

                progress.Value = 95;
            }
            finally
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        private static string GetInstallDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), SetupProgram.ProductName);
        }

        private static void LaunchControlUnelevated(string controlPath)
        {
            // Setup runs elevated so it can write Program Files, System32 and HKLM.
            // Launch through the existing Explorer shell so the post-install control
            // panel runs at the interactive user's normal integrity level instead of
            // inheriting Setup's administrator token.
            string explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "explorer.exe"
            );

            Process.Start(new ProcessStartInfo(explorer, "\"" + controlPath + "\"")
            {
                UseShellExecute = true
            });
        }

        private static string SafeCombine(string root, string relative)
        {
            string fullRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid path in installation payload: " + relative);
            return full;
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string rel = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, rel));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                string target = Path.Combine(destination, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static void RegisterUninstall(string installDir)
        {
            string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EminemScreenMate";
            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(keyPath))
            {
                key.SetValue("DisplayName", SetupProgram.ProductName, RegistryValueKind.String);
                key.SetValue("DisplayVersion", SetupProgram.Version, RegistryValueKind.String);
                key.SetValue("Publisher", "Preservation compatibility package", RegistryValueKind.String);
                key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                key.SetValue("DisplayIcon", Path.Combine(installDir, "Eminem ScreenMate Control.exe"), RegistryValueKind.String);
                key.SetValue("UninstallString", "\"" + Path.Combine(installDir, "Uninstall Eminem ScreenMate.exe") + "\"", RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", 8500, RegistryValueKind.DWord);
            }
        }

        private static void CreateStartMenuShortcuts(string installDir)
        {
            string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), SetupProgram.ProductName);
            Directory.CreateDirectory(menuDir);

            CreateShortcut(
                Path.Combine(menuDir, "Eminem ScreenMate.lnk"),
                Path.Combine(installDir, "Eminem ScreenMate Control.exe"),
                "",
                installDir
            );

            CreateShortcut(
                Path.Combine(menuDir, "Uninstall Eminem ScreenMate.lnk"),
                Path.Combine(installDir, "Uninstall Eminem ScreenMate.exe"),
                "",
                installDir
            );
        }

        private static void CreateShortcut(string linkPath, string target, string arguments, string workingDirectory)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
                shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDirectory });
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
