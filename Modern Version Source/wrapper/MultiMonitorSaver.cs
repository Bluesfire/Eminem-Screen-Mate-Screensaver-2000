using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;

// Coordinates original WineVDM instances. The original program renders every
// frame and plays every sound. The primary original RNG chooses actions.
internal sealed class MultiMonitorSaver : ApplicationContext
{
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private readonly List<MonitorInstance> instances = new List<MonitorInstance>();
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly bool test;
    private readonly int duration;
    private readonly string snapshots;
    private Point initialMouse;
    private bool closing;
    private readonly string introEventName = "Local\\EminemScreenMate-" + Guid.NewGuid().ToString("N");
    private readonly EventWaitHandle introEvent;
    private MemoryMappedFile coordination;
    private Bitmap album;
    private MonitorInstance primary;
    private bool albumSeen, destruction;
    private int albumAbsent;
    private long nextIntroProbe;
    private delegate bool EnumProc(IntPtr window,IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback,IntPtr state);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window,StringBuilder name,int count);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);

    internal static void EnablePhysicalCoordinates()
    {
        try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; }
        catch (EntryPointNotFoundException) { }
        SetProcessDPIAware();
    }
    internal static void Run(string runtime, bool test, bool introTest, int duration)
    {
        using (MultiMonitorSaver saver = new MultiMonitorSaver(runtime, test, duration)) Application.Run(saver);
    }
    private static string Root(string runtime) { return Directory.GetParent(Directory.GetParent(Directory.GetParent(runtime).FullName).FullName).FullName; }
    internal static void SelfTest(string runtime)
    {
        string root=Root(runtime);
        foreach (string name in new string[] {"ScreenMateMonitorHost.exe", "ScreenMateMonitorHooks.dll"})
        {
            byte[] binary=File.ReadAllBytes(Path.Combine(root,name));
            int header=BitConverter.ToInt32(binary,60);
            if(BitConverter.ToUInt16(binary,header+4)!=0x14c) throw new Exception(name+" must be x86 for WineVDM.");
        }
        string[] names={"eminem.SCR","eminem.IMX","SYSTEM/ox16sys.dll","SYSTEM/wavex16b.dll"};
        string[] hashes={"ed078729b12027d1ad2ab49f6ad1485b738ead52b97c1d5285a938283f4c65a8","96e7871d61d9b983b8f7fcd234bde5cdcd218d900c1f92701f505ed98aed4eff","8ecc2d0a97d6285b2674c2807c37841ff7ba9413979e36b207d3da497d0a0197","f45e318f4e7b8ce0e67af27230c2ede8bb69c05802b94ae1bd78fbcd593b50ba"};
        for(int i=0;i<names.Length;i++)
        {
            using(SHA256 hash=SHA256.Create())
            using(FileStream file=File.OpenRead(Path.Combine(runtime,"WINDOWS",names[i])))
                if(BitConverter.ToString(hash.ComputeHash(file)).Replace("-","").ToLowerInvariant()!=hashes[i]) throw new Exception("Original "+names[i]+" has changed.");
        }
        string diagnostics=Path.Combine(root,"analysis");
        if(Directory.Exists(diagnostics)) File.WriteAllText(Path.Combine(diagnostics,"self-test.txt"),"PASS: original SCR, IMX, interpreter and mixer match preservation hashes; both process-local monitor adapters are x86.\r\n");
    }
    private MultiMonitorSaver(string runtime, bool testMode, int testDuration)
    {
        test=testMode; duration=testDuration;
        introEvent=new EventWaitHandle(false,EventResetMode.ManualReset,introEventName);
        snapshots=Path.Combine(Path.GetTempPath(),"EminemScreenMate-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshots);
        try
        {
            album=LoadAlbum(Path.Combine(runtime,"WINDOWS","eminem.IMX"));
            string host=Path.Combine(Root(runtime),"ScreenMateMonitorHost.exe");
            if(!File.Exists(host)||!File.Exists(Path.Combine(Root(runtime),"ScreenMateMonitorHooks.dll")))
                throw new FileNotFoundException("The original-engine monitor adapters are missing. Please rebuild or reinstall Eminem ScreenMate.");
            // Capture every display first, before ANY saver window exists.
            foreach(Screen screen in Screen.AllScreens)
            {
                Bitmap desktop=new Bitmap(screen.Bounds.Width,screen.Bounds.Height,PixelFormat.Format32bppRgb);
                using(Graphics g=Graphics.FromImage(desktop)) g.CopyFromScreen(screen.Bounds.Location,Point.Empty,desktop.Size,CopyPixelOperation.SourceCopy);
                string path=Path.Combine(snapshots,instances.Count+".bmp");
                desktop.Save(path,ImageFormat.Bmp);
                MonitorInstance instance=new MonitorInstance(this,screen.Bounds,desktop,path,screen.Primary);
                instances.Add(instance);
                if(screen.Primary) primary=instance;
            }
            initialMouse=Cursor.Position;
            if(instances.Count>64) throw new NotSupportedException("This build supports up to 64 monitors.");
            coordination=MemoryMappedFile.CreateNew(introEventName+"-state",262144);
            using(MemoryMappedViewAccessor state=coordination.CreateViewAccessor())
            {
                state.Write(0,0x534d5031); state.Write(4,instances.Count); state.Write(8,instances.IndexOf(primary));
                for(int i=0;i<9;i++) state.Write(34584+i*4,-1);
            }
            foreach(MonitorInstance instance in instances) instance.Background.Show();
            foreach(MonitorInstance instance in instances)
            {
                ProcessStartInfo start=new ProcessStartInfo(host,"\""+runtime+"\"");
                start.UseShellExecute=false; start.CreateNoWindow=true; start.WorkingDirectory=runtime;
                start.EnvironmentVariables["SCREENMATE_LEFT"]=instance.Bounds.Left.ToString();
                start.EnvironmentVariables["SCREENMATE_TOP"]=instance.Bounds.Top.ToString();
                start.EnvironmentVariables["SCREENMATE_WIDTH"]=instance.Bounds.Width.ToString();
                start.EnvironmentVariables["SCREENMATE_HEIGHT"]=instance.Bounds.Height.ToString();
                start.EnvironmentVariables["SCREENMATE_SNAPSHOT"]=instance.Snapshot;
                start.EnvironmentVariables["SCREENMATE_SECONDARY"]=instance.Primary?"0":"1";
                start.EnvironmentVariables["SCREENMATE_INTRO_EVENT"]=introEventName;
                start.EnvironmentVariables["SCREENMATE_PID_FILE"]=instance.Snapshot+".pid";
                start.EnvironmentVariables["SCREENMATE_PRESERVE"]="1";
                start.EnvironmentVariables["SCREENMATE_COORDINATION"]=introEventName+"-state";
                start.EnvironmentVariables["SCREENMATE_MONITOR_INDEX"]=instances.IndexOf(instance).ToString();
                if(test)
                {
                    string diagnostics=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"analysis","monitor-validation");
                    Directory.CreateDirectory(diagnostics);
                    string log=Path.Combine(diagnostics,"audio-"+instances.IndexOf(instance)+".txt");
                    File.WriteAllText(log,"");
                    start.EnvironmentVariables["SCREENMATE_AUDIO_LOG"]=log;
                }
                instance.Host=Process.Start(start);
                if(instance.Host==null) throw new Exception("The original screensaver could not be started.");
            }
            timer.Interval=100; timer.Tick+=Tick; timer.Start();
        }
        catch { Cleanup(); throw; }
    }
    private void Tick(object sender,EventArgs args)
    {
        try
        {
            if(!test&&(Math.Abs(Cursor.Position.X-initialMouse.X)>8||Math.Abs(Cursor.Position.Y-initialMouse.Y)>8)) { ExitThread(); return; }
            foreach(MonitorInstance instance in instances)
            {
                if(!instance.Host.HasExited) continue;
                int code=instance.Host.ExitCode;
                if(test) SaveTest();
                ExitThread();
                if(code==80) throw new Exception("The original runtime encountered an error. The screensaver was stopped and your desktop was restored.");
                if(code==90) throw new Exception("The preserved runtime did not match the expected original action or audio definitions. The screensaver was stopped and your desktop was restored.");
                if(code!=0) throw new Exception("The original monitor instance exited with code "+code+".");
                return;
            }
            if(!destruction && clock.ElapsedMilliseconds>=nextIntroProbe)
            {
                nextIntroProbe=clock.ElapsedMilliseconds+200;
                CheckIntro();
            }
            if(test&&clock.ElapsedMilliseconds>duration) { SaveTest(); ExitThread(); }
        }
        catch(Exception ex)
        {
            ExitThread();
            if(test) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"analysis","test-error.txt"),ex.ToString()); Environment.ExitCode=1; }
            else MessageBox.Show(ex.Message,"Eminem ScreenMate",MessageBoxButtons.OK,MessageBoxIcon.Error);
        }
    }
    private static Bitmap LoadAlbum(string path)
    {
        byte[] data=File.ReadAllBytes(path);
        for(int offset=0;offset<data.Length-10;offset++)
        {
            if(data[offset]!=255||data[offset+1]!=216||data[offset+2]!=255||data[offset+3]!=224||
               data[offset+6]!='J'||data[offset+7]!='F'||data[offset+8]!='I'||data[offset+9]!='F') continue;
            for(int end=offset+3;end<data.Length-1;end++)
            {
                if(data[end]!=255||data[end+1]!=217) continue;
                using(MemoryStream stream=new MemoryStream(data,offset,end+2-offset))
                using(Bitmap image=new Bitmap(stream)) return new Bitmap(image);
            }
        }
        throw new InvalidDataException("The original album image could not be found.");
    }
    private void CheckIntro()
    {
        Rectangle target=new Rectangle(primary.Bounds.Left+(primary.Bounds.Width-album.Width)/2,
            primary.Bounds.Top+(primary.Bounds.Height-album.Height)/2,album.Width,album.Height);
        int matches=0;
        using(Bitmap visible=new Bitmap(album.Width,album.Height))
        using(Graphics g=Graphics.FromImage(visible))
        {
            g.CopyFromScreen(target.Location,Point.Empty,visible.Size,CopyPixelOperation.SourceCopy);
            for(int row=0;row<7;row++) for(int col=0;col<7;col++)
            {
                int x=(2*col+1)*album.Width/14,y=(2*row+1)*album.Height/14;
                Color actual=visible.GetPixel(x,y),expected=album.GetPixel(x,y);
                if(Math.Abs(actual.R-expected.R)<=36&&Math.Abs(actual.G-expected.G)<=36&&Math.Abs(actual.B-expected.B)<=36) matches++;
            }
        }
        if(matches>=43) { albumSeen=true; albumAbsent=0; return; }
        if(!albumSeen||++albumAbsent<2) return;
        // Recognition only gates visibility of the SECONDARY original engines.
        // Main playback and all original frames/media continue unchanged.
        introEvent.Set();
        foreach(MonitorInstance instance in instances)
        {
            if(instance.Primary) continue;
            uint pid=UInt32.Parse(File.ReadAllText(instance.Snapshot+".pid"));
            IntPtr saver=IntPtr.Zero;
            EnumWindows(delegate(IntPtr window,IntPtr state) {
                uint owner; GetWindowThreadProcessId(window,out owner);
                if(owner!=pid) return true;
                StringBuilder name=new StringBuilder(256); GetClassName(window,name,256);
                if(name.ToString().IndexOf("WindowsScreenSaverClass",StringComparison.OrdinalIgnoreCase)<0) return true;
                saver=window; return false;
            },IntPtr.Zero);
            if(saver==IntPtr.Zero) throw new Exception("The original secondary saver window could not be found.");
            SetWindowPos(saver,new IntPtr(-1),0,0,0,0,0x53);
        }
        destruction=true;
    }
    private void SaveTest()
    {
        string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"analysis","monitor-validation");
        Directory.CreateDirectory(directory);
        StringBuilder report=new StringBuilder("Original engines only; elapsed ms="+clock.ElapsedMilliseconds+"; album seen="+albumSeen+"; secondary destruction enabled="+destruction+Environment.NewLine);
        using(MemoryMappedViewAccessor state=coordination.CreateViewAccessor())
        {
            report.AppendLine("Shared action opportunities="+state.ReadInt32(12)+"; audio errors="+state.ReadInt32(16+7*64*4));
            for(int i=0;i<instances.Count;i++) report.AppendLine("Monitor "+i+": original root ticks="+state.ReadInt32(16+64*4+i*4)+"; admitted selections="+state.ReadInt32(16+2*64*4+i*4)+"; effects forwarded="+state.ReadInt32(16+5*64*4+i*4)+"; played by main="+state.ReadInt32(16+6*64*4+i*4));
        }
        for(int i=0;i<instances.Count;i++)
        {
            MonitorInstance instance=instances[i];
            using(Bitmap image=new Bitmap(instance.Bounds.Width,instance.Bounds.Height))
            using(Graphics g=Graphics.FromImage(image))
            { g.CopyFromScreen(instance.Bounds.Location,Point.Empty,image.Size,CopyPixelOperation.SourceCopy); image.Save(Path.Combine(directory,"monitor-"+i+".png")); }
            report.AppendLine(i+": "+instance.Bounds+" original host="+instance.Host.Id+" exited="+instance.Host.HasExited);
        }
        File.WriteAllText(Path.Combine(directory,"report.txt"),report.ToString());
    }
    private void Cleanup()
    {
        if(closing) return;
        closing=true; timer.Stop();
        foreach(MonitorInstance instance in instances)
        {
            if(instance.Host!=null)
            {
                try { if(!instance.Host.HasExited) { instance.Host.Kill(); instance.Host.WaitForExit(2000); } }
                catch(InvalidOperationException) { }
                catch(System.ComponentModel.Win32Exception) { }
                finally { instance.Host.Dispose(); }
            }
            instance.Background.Close(); instance.Background.Dispose();
            try {
                if(File.Exists(instance.Snapshot)) File.Delete(instance.Snapshot);
                if(File.Exists(instance.Snapshot+".pid")) File.Delete(instance.Snapshot+".pid");
            } catch(IOException) { }
        }
        try { if(Directory.Exists(snapshots)) Directory.Delete(snapshots,false); } catch(IOException) { }
        timer.Dispose();
        introEvent.Dispose();
        if(coordination!=null) coordination.Dispose();
        if(album!=null) album.Dispose();
    }
    protected override void ExitThreadCore() { Cleanup(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing) { if(disposing) Cleanup(); base.Dispose(disposing); }
    private sealed class MonitorInstance
    {
        internal Rectangle Bounds; internal string Snapshot; internal Process Host; internal BackgroundForm Background; internal bool Primary;
        internal MonitorInstance(MultiMonitorSaver owner,Rectangle bounds,Bitmap desktop,string snapshot,bool primary)
        { Bounds=bounds; Snapshot=snapshot; Background=new BackgroundForm(owner,bounds,desktop); Primary=primary; }
    }
    private sealed class BackgroundForm:Form
    {
        private readonly Bitmap desktop;
        internal BackgroundForm(MultiMonitorSaver owner,Rectangle bounds,Bitmap desktop)
        {
            this.desktop=desktop; FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false;
            StartPosition=FormStartPosition.Manual; AutoScaleMode=AutoScaleMode.None; Bounds=bounds; TopMost=true; DoubleBuffered=true;
            KeyDown+=delegate { if(!owner.test) owner.ExitThread(); };
            MouseDown+=delegate { if(!owner.test) owner.ExitThread(); };
            MouseWheel+=delegate { if(!owner.test) owner.ExitThread(); };
            FormClosed+=delegate { if(!owner.closing) owner.ExitThread(); };
        }
        protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(desktop,0,0); }
        protected override void Dispose(bool disposing) { if(disposing) desktop.Dispose(); base.Dispose(disposing); }
    }
}

