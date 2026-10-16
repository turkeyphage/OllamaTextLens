using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace OllamaTextLens;

/// <summary>Own and release this object on the startup/UI thread: Windows mutex ownership is thread-affine.</summary>
internal sealed class SingleInstance : IDisposable
{
    readonly Mutex startupGate;
    readonly Mutex running;
    readonly EventWaitHandle shutdown;
    RegisteredWaitHandle? listener;
    bool ownsRunning;

    internal SingleInstance(string? testScope = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var scope = testScope ?? WindowsIdentity.GetCurrent().User!.Value;
        // Local namespace isolates Windows sessions; the SID isolates users.
        var name = @"Local\OllamaTextLens." + scope;
        startupGate = new Mutex(false, name + ".Startup");
        running = new Mutex(false, name + ".Running");
        shutdown = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Shutdown");
    }

    static bool Take(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        catch (AbandonedMutexException) { return true; } // A crashed owner has already released its OS resources.
    }

    internal bool TryAcquire(TimeSpan timeout, Func<bool>? closeLegacy = null)
    {
        if (ownsRunning) throw new InvalidOperationException("This process already owns the instance lock.");
        // Serialize rapid double-clicks: only one launcher can replace the running app at a time.
        if (!Take(startupGate, timeout)) return false;
        try
        {
            if (!Take(running, TimeSpan.Zero))
            {
                shutdown.Set();
                if (!Take(running, timeout)) return false;
            }
            ownsRunning = true;
            if (closeLegacy != null && !closeLegacy())
            {
                running.ReleaseMutex();
                ownsRunning = false;
                return false;
            }
            shutdown.Reset();
            return true;
        }
        finally { startupGate.ReleaseMutex(); }
    }

    internal void ListenForReplacement(Action requestShutdown)
    {
        if (!ownsRunning || listener != null) throw new InvalidOperationException("Acquire the instance lock before listening.");
        listener = ThreadPool.RegisterWaitForSingleObject(shutdown, (_, _) => requestShutdown(), null, Timeout.Infinite, true);
    }

    internal static bool CloseLegacyInstances()
    {
        using var self = Process.GetCurrentProcess();
        var deadline = Stopwatch.StartNew();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self.Id ||
                        !Regex.IsMatch(process.ProcessName, @"^OllamaTextLens(?:-\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?-win-x64)?$", RegexOptions.IgnoreCase) ||
                        process.SessionId != self.SessionId) continue;
                    // Do not close unrelated programs solely because they share a process name.
                    var metadata=process.MainModule?.FileVersionInfo;
                    if (metadata?.ProductName != "OllamaTextLens") continue;
                    var version=new Version(metadata.FileMajorPart,metadata.FileMinorPart,metadata.FileBuildPart,metadata.FilePrivatePart);
                    bool legacyRelease=version < new Version(0,2,1,0);
                    if (process.MainWindowHandle == IntPtr.Zero && !legacyRelease) continue; // A concurrent new launcher has no window yet.
                    if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow();
                    // Older releases can remain alive without a window after closing a popup.
                    // Only verified pre-0.2.1 app processes may be terminated as a migration fallback.
                    if (legacyRelease && !process.WaitForExit(1500)) process.Kill();
                    int remaining = Math.Max(0, 8000 - (int)deadline.ElapsedMilliseconds);
                    if (!process.WaitForExit(remaining)) return false;
                }
                catch (InvalidOperationException) { /* Process exited between enumeration and inspection. */ }
                catch (System.ComponentModel.Win32Exception) { return false; }
            }
        }
        return true;
    }

    public void Dispose()
    {
        listener?.Unregister(null);
        listener = null;
        if (ownsRunning) { running.ReleaseMutex(); ownsRunning = false; }
        shutdown.Dispose(); running.Dispose(); startupGate.Dispose();
    }
}
