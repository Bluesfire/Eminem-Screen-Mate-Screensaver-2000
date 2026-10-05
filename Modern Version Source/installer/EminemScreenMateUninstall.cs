using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Uninstall Eminem ScreenMate")]
[assembly: AssemblyDescription("Uninstaller for the recovered 2000 Eminem ScreenMate compatibility package")]
[assembly: AssemblyCompany("Preservation compatibility package")]
[assembly: AssemblyProduct("Eminem ScreenMate")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

internal static class UninstallProgram
{
    private const int MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existingFileName, string newFileName, int flags);

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string installDir = ReadInstallDir();
        if (String.IsNullOrEmpty(installDir))
            installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        bool fromTemp = args.Length >= 2 && String.Equals(args[0], "/run-from-temp", StringComparison.OrdinalIgnoreCase);
        if (!fromTemp && IsInside(AppDomain.CurrentDomain.BaseDirectory, installDir))
        {
            string tempExe = Path.Combine(Path.GetTempPath(), "EminemScreenMateUninstall_" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, tempExe, true);
            Process.Start(new ProcessStartInfo(tempExe, "/run-from-temp \"" + installDir + "\"") { UseShellExecute = true });
            return;
        }

        if (fromTemp)
            installDir = args[1];

        DialogResult answer = MessageBox.Show(
            "Remove Eminem ScreenMate and its Windows compatibility runtime?",
            "Uninstall Eminem ScreenMate",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2
        );
        if (answer != DialogResult.Yes)
            return;

        try
        {
            StopProductProcesses(installDir);
            bool wasCurrentSaver = DisableIfCurrent();

            string systemScr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Eminem ScreenMate.scr");
            if (File.Exists(systemScr))
                File.Delete(systemScr);

            Registry.LocalMachine.DeleteSubKeyTree(@"Software\EminemScreenMate", false);
            Registry.LocalMachine.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\EminemScreenMate", false);

            string menuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Eminem ScreenMate");
            if (Directory.Exists(menuDir))
                Directory.Delete(menuDir, true);

            if (Directory.Exists(installDir))
                Directory.Delete(installDir, true);

            FinalizeDisabledState(wasCurrentSaver);

            MessageBox.Show("Eminem ScreenMate was removed.", "Eminem ScreenMate", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Uninstall could not be completed.\n\n" + ex.Message, "Eminem ScreenMate", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (fromTemp)
                MoveFileEx(Application.ExecutablePath, null, MOVEFILE_DELAY_UNTIL_REBOOT);
        }
    }

    private static string ReadInstallDir()
    {
        try
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"Software\EminemScreenMate"))
                return key == null ? null : key.GetValue("InstallDir") as string;
        }
        catch { return null; }
    }

    private static bool DisableIfCurrent()
    {
        string systemScr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Eminem ScreenMate.scr");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
        {
            string current = key.GetValue("SCRNSAVE.EXE") as string;
            if (PathsEqual(current, systemScr))
            {
                key.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                key.DeleteValue("SCRNSAVE.EXE", false);
                return true;
            }
        }
        return false;
    }

    private static void FinalizeDisabledState(bool wasCurrentSaver)
    {
        if (!wasCurrentSaver)
            return;

        string systemScr = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Eminem ScreenMate.scr");
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
        {
            string current = key.GetValue("SCRNSAVE.EXE") as string;
            if (String.IsNullOrWhiteSpace(current) || PathsEqual(current, systemScr))
            {
                key.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                key.DeleteValue("SCRNSAVE.EXE", false);
            }
        }
    }

    private static void StopProductProcesses(string installDir)
    {
        string runtimeExe = Path.Combine(installDir, @"compat\otvdm\otvdm-v0.9.0\otvdmw.exe");
        string controlExe = Path.Combine(installDir, "Eminem ScreenMate Control.exe");
        string installedSaver = Path.Combine(installDir, "Eminem ScreenMate.scr");
        string systemSaver = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\Eminem ScreenMate.scr");

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                string path = process.MainModule.FileName;
                if (PathsEqual(path, runtimeExe) ||
                    PathsEqual(path, controlExe) ||
                    PathsEqual(path, installedSaver) ||
                    PathsEqual(path, systemSaver))
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }
    }

    private static bool IsInside(string path, string root)
    {
        if (String.IsNullOrWhiteSpace(path) || String.IsNullOrWhiteSpace(root))
            return false;
        try
        {
            string fullPath = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            string fullRoot = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static bool PathsEqual(string a, string b)
    {
        if (String.IsNullOrWhiteSpace(a) || String.IsNullOrWhiteSpace(b))
            return false;
        try { return String.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase); }
        catch { return String.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase); }
    }
}
