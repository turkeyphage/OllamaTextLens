using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

internal static class AppLifetimeTests
{
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    internal static void Run(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path=Path.GetFullPath(executable);
        if(!File.Exists(path)) throw new FileNotFoundException("Build the Windows application before running the lifecycle test.",path);
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
                        if(window!=IntPtr.Zero && app.MainWindowTitle=="OllamaTextLens") break;
                        if(timer.Elapsed>TimeSpan.FromSeconds(25)) throw new Exception("Settings window did not appear.");
                        Thread.Sleep(100);
                    }
                    if(previous!=null)
                    {
                        if(!previous.WaitForExit(5000) || previous.ExitCode!=0) throw new Exception("Hidden previous instance did not exit cleanly.");
                        previous.Dispose(); previous=null;
                        Console.WriteLine("PASS relaunch replaces the hidden instance and reopens settings");
                    }
                    if(!app.CloseMainWindow()) throw new Exception("Could not close settings.");
                    timer.Restart();
                    while(IsWindowVisible(window))
                    {
                        if(timer.Elapsed>TimeSpan.FromSeconds(5)) throw new Exception("Settings did not hide to the tray.");
                        Thread.Sleep(100);
                    }
                    if(app.WaitForExit(1000)) throw new Exception("Closing settings exited instead of staying in the tray.");
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
