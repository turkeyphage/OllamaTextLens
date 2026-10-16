using System.Diagnostics;

internal static class AppLifetimeTests
{
    internal static void Run(string executable)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var path=Path.GetFullPath(executable);
        if(!File.Exists(path)) throw new FileNotFoundException("Build the Windows application before running the lifecycle test.",path);
        // Use the real WPF application, not a simulated shutdown callback.
        for(int attempt=1;attempt<=2;attempt++)
        {
            using var app=Process.Start(new ProcessStartInfo(path) { UseShellExecute=false })!;
            try
            {
                var timer=Stopwatch.StartNew();
                while(true)
                {
                    app.Refresh();
                    if(app.HasExited) throw new Exception("Application exited before its main window appeared.");
                    if(app.MainWindowHandle!=IntPtr.Zero && app.MainWindowTitle=="OllamaTextLens") break;
                    if(timer.Elapsed>TimeSpan.FromSeconds(25)) throw new Exception("Application did not show its main settings window.");
                    Thread.Sleep(100);
                }
                if(!app.CloseMainWindow()) throw new Exception("Could not close the main settings window.");
                if(!app.WaitForExit(10000)) throw new Exception("Closing the main settings window left the application process running.");
                if(app.ExitCode!=0) throw new Exception("Application did not exit cleanly.");
                Console.WriteLine(attempt==1?"PASS closing the real main window exits the process":"PASS application can reopen and exit after releasing the instance lock");
            }
            finally
            {
                if(!app.HasExited) { app.Kill(); app.WaitForExit(5000); } // Only the application process started by this test.
            }
        }
    }
}
