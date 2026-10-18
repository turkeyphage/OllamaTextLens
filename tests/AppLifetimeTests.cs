using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

internal static class AppLifetimeTests
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int count);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon,out IconInfo info);
    [DllImport("gdi32.dll",EntryPoint="GetObjectW")] static extern int GetObject(IntPtr bitmap,int size,out BitmapInfo info);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [StructLayout(LayoutKind.Sequential)] struct IconInfo { public int IsIcon; public uint X,Y; public IntPtr Mask,Color; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public int Type,Width,Height,WidthBytes; public ushort Planes,BitsPixel; public IntPtr Bits; }
    static void CheckWindowIcons(IntPtr window)
    {
        foreach(var (kind,minimum) in new[]{(0,16),(1,32)})
        {
            var icon=SendMessage(window,0x007F,(IntPtr)kind,IntPtr.Zero);
            if(icon==IntPtr.Zero || !GetIconInfo(icon,out var info)) throw new Exception("Window icon missing.");
            try
            {
                if(GetObject(info.Color,Marshal.SizeOf<BitmapInfo>(),out var bitmap)==0 || bitmap.Width<minimum)
                    throw new Exception($"Window icon {kind} is smaller than {minimum}px.");
                Console.WriteLine($"PASS native window icon {kind}: {bitmap.Width}x{bitmap.Height}px");
            }
            finally { if(info.Mask!=IntPtr.Zero) DeleteObject(info.Mask); if(info.Color!=IntPtr.Zero) DeleteObject(info.Color); }
        }
    }
    static void CheckExecutableIcon(string path)
    {
        var expected=File.ReadAllBytes(Path.GetFullPath("Assets/app.ico"));
        var executable=File.ReadAllBytes(path);
        int count=BitConverter.ToUInt16(expected,4);
        for(int i=0;i<count;i++)
        {
            int length=BitConverter.ToInt32(expected,6+i*16+8), offset=BitConverter.ToInt32(expected,6+i*16+12);
            if(executable.AsSpan().IndexOf(expected.AsSpan(offset,length))<0)
                throw new Exception("EXE contains stale or missing icon resources. Rebuild the application host.");
        }
        Console.WriteLine($"PASS EXE embeds all {count} current main-logo frames");
    }
    internal static void Run(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path=Path.GetFullPath(executable);
        if(!File.Exists(path)) throw new FileNotFoundException("Build the Windows application before running the lifecycle test.",path);
        CheckExecutableIcon(path);
        Process? previous=null;
        try
        {
            for(int attempt=1;attempt<=2;attempt++)
            {
                var app=Process.Start(new ProcessStartInfo(path) { UseShellExecute=false })!;
                try
                {
                    var timer=Stopwatch.StartNew();
                    IntPtr window;
                    while(true)
                    {
                        app.Refresh();
                        if(app.HasExited) throw new Exception("Application exited before settings appeared.");
                        window=app.MainWindowHandle;
                        if(window!=IntPtr.Zero && app.MainWindowTitle=="OllamaTextLens")
                        {
                            var windowClass=new System.Text.StringBuilder(256);
                            GetClassName(window,windowClass,windowClass.Capacity);
                            if(windowClass.ToString()=="#32770") throw new Exception("Application showed a startup error dialog instead of settings.");
                            break;
                        }
                        if(timer.Elapsed>TimeSpan.FromSeconds(25)) throw new Exception("Settings window did not appear.");
                        Thread.Sleep(100);
                    }
                    if(previous!=null)
                    {
                        if(!previous.WaitForExit(5000) || previous.ExitCode!=0) throw new Exception("Hidden previous instance did not exit cleanly.");
                        previous.Dispose(); previous=null;
                        Console.WriteLine("PASS relaunch replaces the hidden instance and reopens settings");
                    }
                    CheckWindowIcons(window);
                    if(!app.CloseMainWindow()) throw new Exception("Could not close settings.");
                    timer.Restart();
                    while(IsWindowVisible(window))
                    {
                        if(timer.Elapsed>TimeSpan.FromSeconds(5)) throw new Exception("Settings did not hide to the tray.");
                        Thread.Sleep(100);
                    }
                    if(app.WaitForExit(1000)) throw new Exception($"Closing settings exited instead of staying in the tray (exit code {app.ExitCode}).");
                    Console.WriteLine("PASS closing settings hides the window and keeps the process alive");
                    if(attempt==2)
                    {
                        // The same graceful shutdown path used by replacement must bypass close-to-tray.
                        using var signal=EventWaitHandle.OpenExisting(@"Local\OllamaTextLens."+WindowsIdentity.GetCurrent().User!.Value+".Shutdown");
                        signal.Set();
                        if(!app.WaitForExit(10000) || app.ExitCode!=0) throw new Exception("Explicit shutdown did not fully exit the tray application.");
                        Console.WriteLine("PASS explicit shutdown exits the hidden app and releases its resources");
                    }
                    else { previous=app; app=null!; }
                }
                finally { if(app!=null) { if(!app.HasExited) {app.Kill();app.WaitForExit(5000);} app.Dispose(); } }
            }
        }
        finally { if(previous!=null) {if(!previous.HasExited) {previous.Kill();previous.WaitForExit(5000);} previous.Dispose();} }
    }
}
