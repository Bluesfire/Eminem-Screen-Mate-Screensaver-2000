using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Eminem ScreenMate Screen Saver")]
[assembly: AssemblyDescription("Windows 10/11 compatibility wrapper for the original 2000 Eminem ScreenMate")]
[assembly: AssemblyCompany("Preservation compatibility package")]
[assembly: AssemblyProduct("Eminem ScreenMate")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

internal static class Program
{
    private const string RegistryPath = @"Software\EminemScreenMate";
    private const string RuntimeRelativePath = @"compat\otvdm\otvdm-v0.9.0";
    private const string PayloadRelativePath = @"WINDOWS\eminem.SCR";

    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000L;
    private const long WS_VISIBLE = 0x10000000L;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            MultiMonitorSaver.EnablePhysicalCoordinates();
            IntPtr previewParent;
            if (TryGetPreviewParent(args, out previewParent))
            {
                RunPreview(previewParent);
                return;
            }

            if (IsConfigureRequest(args))
            {
                MessageBox.Show(
                    "The original 2000 Eminem ScreenMate has no configurable options.\n\n" +
                    "Use Windows Screen Saver Settings to choose the wait time and resume behavior.",
                    "Eminem ScreenMate",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            string root = FindInstallRoot();
            if (root == null)
            {
                ShowError(
                    "The Eminem ScreenMate compatibility runtime could not be found.\n\n" +
                    "Keep this .scr in the Eminem ScreenMate folder, or run the installer again."
                );
                return;
            }

            string runtime = Path.Combine(root, RuntimeRelativePath);
            if (args.Length > 0 && args[0] == "--self-test")
            {
                MultiMonitorSaver.SelfTest(runtime);
                return;
            }
            if (args.Length > 0 && (args[0].Equals("/s", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-s", StringComparison.OrdinalIgnoreCase) || args[0].StartsWith("/s:", StringComparison.OrdinalIgnoreCase) || args[0] == "--monitor-test" || args[0] == "--intro-test" || args[0] == "--intro-probe" || args[0] == "--validation-test"))
            {
                MultiMonitorSaver.Run(runtime, args[0].StartsWith("--"), args[0].StartsWith("--intro"), args[0] == "--validation-test" ? 120000 : args[0] == "--intro-test" ? 60000 : 16000);
                return;
            }
            string otvdm = Path.Combine(runtime, "otvdmw.exe");
            string payload = Path.Combine(runtime, PayloadRelativePath);

            if (!File.Exists(otvdm) || !File.Exists(payload))
            {
                ShowError("The WineVDM runtime or original eminem.SCR payload is missing.");
                return;
            }

            string[] forwarded = args;
            if (forwarded == null || forwarded.Length == 0)
                forwarded = new string[] { "/c" };

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = otvdm;
            psi.WorkingDirectory = runtime;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.Arguments = BuildArguments(PayloadRelativePath, forwarded);

            Process child = Process.Start(psi);
            if (child != null)
            {
                child.WaitForExit();
                child.Dispose();
            }
        }
        catch (Exception ex)
        {
            if (args.Length > 0 && args[0].StartsWith("--"))
            {
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "analysis", "test-error.txt"), ex.ToString());
                Environment.ExitCode = 1;
                return;
            }
            ShowError("Eminem ScreenMate could not be started.\n\n" + ex.Message);
        }
    }

    private static bool IsConfigureRequest(string[] args)
    {
        if (args == null || args.Length == 0 || String.IsNullOrEmpty(args[0]))
            return true;

        string first = args[0].Trim();
        return first.StartsWith("/c", StringComparison.OrdinalIgnoreCase) ||
               first.StartsWith("-c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetPreviewParent(string[] args, out IntPtr parent)
    {
        parent = IntPtr.Zero;
        if (args == null || args.Length == 0 || String.IsNullOrEmpty(args[0]))
            return false;

        string first = args[0].Trim();
        if (!(first.StartsWith("/p", StringComparison.OrdinalIgnoreCase) ||
              first.StartsWith("-p", StringComparison.OrdinalIgnoreCase)))
            return false;

        string handleText = null;
        int colon = first.IndexOf(':');
        if (colon >= 0 && colon + 1 < first.Length)
            handleText = first.Substring(colon + 1);
        else if (args.Length > 1)
            handleText = args[1];

        long handleValue;
        if (!String.IsNullOrWhiteSpace(handleText) && Int64.TryParse(handleText.Trim(), out handleValue))
            parent = new IntPtr(handleValue);

        return true;
    }

    private static void RunPreview(IntPtr parent)
    {
        // The original 2000 Win16 saver predates the modern /p embedded-preview
        // contract and paints over the Screen Saver Settings dialog if forwarded.
        // Render a harmless native child preview instead; /s still runs the
        // untouched original saver through WineVDM.
        if (parent == IntPtr.Zero)
            return;

        RECT rect;
        if (!GetClientRect(parent, out rect))
            return;

        PreviewForm form = new PreviewForm();
        form.CreateControl();
        SetParent(form.Handle, parent);

        long style = WS_CHILD | WS_VISIBLE;
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(form.Handle, GWL_STYLE, new IntPtr(style));
        else
            SetWindowLong32(form.Handle, GWL_STYLE, unchecked((int)style));

        form.Bounds = new Rectangle(0, 0, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
        form.Show();
        Application.Run(form);
    }

    private static string FindInstallRoot()
    {
        string besideWrapper = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (HasRuntime(besideWrapper))
            return besideWrapper;

        string registered = ReadRegisteredInstallRoot(Registry.LocalMachine);
        if (!String.IsNullOrEmpty(registered) && HasRuntime(registered))
            return registered;

        registered = ReadRegisteredInstallRoot(Registry.CurrentUser);
        if (!String.IsNullOrEmpty(registered) && HasRuntime(registered))
            return registered;

        return null;
    }

    private static string ReadRegisteredInstallRoot(RegistryKey hive)
    {
        try
        {
            using (RegistryKey key = hive.OpenSubKey(RegistryPath, false))
            {
                if (key == null)
                    return null;

                return key.GetValue("InstallDir") as string;
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool HasRuntime(string root)
    {
        if (String.IsNullOrEmpty(root))
            return false;

        string runtime = Path.Combine(root, RuntimeRelativePath);
        return File.Exists(Path.Combine(runtime, "otvdmw.exe")) &&
               File.Exists(Path.Combine(runtime, PayloadRelativePath));
    }

    private static string BuildArguments(string payload, string[] args)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(QuoteArgument(payload));
        for (int i = 0; i < args.Length; i++)
        {
            sb.Append(' ');
            sb.Append(QuoteArgument(args[i]));
        }
        return sb.ToString();
    }

    private static string QuoteArgument(string value)
    {
        if (value == null)
            return "\"\"";

        if (value.Length > 0 && value.IndexOfAny(new char[] { ' ', '\t', '\"' }) < 0)
            return value;

        StringBuilder sb = new StringBuilder();
        sb.Append('\"');
        int backslashes = 0;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '\"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('\"');
                backslashes = 0;
                continue;
            }

            sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('\"');
        return sb.ToString();
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(
            message,
            "Eminem ScreenMate",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
    }

    private sealed class PreviewForm : Form
    {
        public PreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            const string title = "EMINEM\nSCREENMATE";
            using (Font font = new Font("Arial", 9.0f, FontStyle.Bold, GraphicsUnit.Point))
            using (Brush brush = new SolidBrush(Color.White))
            {
                StringFormat format = new StringFormat();
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                e.Graphics.DrawString(title, font, brush, ClientRectangle, format);
                format.Dispose();
            }
        }
    }
}
