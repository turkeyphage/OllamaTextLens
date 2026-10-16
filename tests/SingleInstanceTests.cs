using System.Diagnostics;
using OllamaTextLens;

internal static class SingleInstanceTests
{
    internal static bool TryRunProbe(string[] args)
    {
        if (args.Length != 3 || args[0] != "--instance-probe") return false;
        using var instance = new SingleInstance(args[2]);
        if (!instance.TryAcquire(TimeSpan.FromSeconds(8))) throw new Exception("Probe could not acquire the instance lock.");
        using var stop = new ManualResetEventSlim();
        if (args[1] == "graceful") instance.ListenForReplacement(stop.Set);
        Console.WriteLine("READY"); Console.Out.Flush();
        if (args[1] == "abandon") Environment.Exit(0);
        if (!stop.Wait(TimeSpan.FromSeconds(20))) throw new Exception("Probe did not receive a shutdown request.");
        return true;
    }

    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    internal static void Run()
    {
        if (!OperatingSystem.IsWindows()) return;
        string Scope() => "test." + Guid.NewGuid().ToString("N");
        {
            var scope = Scope();
            using var replacement = new SingleInstance(scope);
            using var old = new Probe("graceful", scope);
            old.Ready();
            Check(replacement.TryAcquire(TimeSpan.FromSeconds(5)), "new instance requests graceful shutdown before acquiring the lock");
            Check(old.Process.WaitForExit(5000) && old.Process.ExitCode == 0, "old instance exits cleanly");
        }
        {
            var scope = Scope();
            using var replacement = new SingleInstance(scope);
            using var old = new Probe("hold", scope);
            old.Ready();
            Check(!replacement.TryAcquire(TimeSpan.FromMilliseconds(150)), "unresponsive owner prevents a duplicate instance");
            old.Stop();
            Check(replacement.TryAcquire(TimeSpan.FromSeconds(3)), "instance lock recovers after owner termination");
            using var unexpectedShutdown = new ManualResetEventSlim();
            replacement.ListenForReplacement(unexpectedShutdown.Set);
            Check(!unexpectedShutdown.Wait(200), "stale shutdown signal is cleared after takeover");
        }
        {
            var scope = Scope();
            using var replacement = new SingleInstance(scope);
            using var crashed = new Probe("abandon", scope);
            crashed.Ready();
            Check(crashed.Process.WaitForExit(5000), "crashed probe exits");
            Check(replacement.TryAcquire(TimeSpan.FromSeconds(3)), "abandoned mutex does not prevent restart");
        }
        {
            var scope = Scope();
            using var first = new Probe("graceful", scope);
            first.Ready();
            // Both launchers start before either announces ownership.
            using var second = new Probe("graceful", scope);
            using var third = new Probe("graceful", scope);
            second.Ready(); third.Ready();
            Check(first.Process.WaitForExit(5000), "rapid relaunch closes the first owner");
            // Either launcher may win the startup gate; exactly one must remain alive.
            var watch = Stopwatch.StartNew();
            while (!second.Process.HasExited && !third.Process.HasExited && watch.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(20);
            Check(second.Process.HasExited != third.Process.HasExited, "concurrent launches leave exactly one running owner");
        }
    }

    sealed class Probe : IDisposable
    {
        internal Process Process { get; }
        internal Probe(string mode, string scope)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(typeof(SingleInstanceTests).Assembly.Location);
            start.ArgumentList.Add("--instance-probe"); start.ArgumentList.Add(mode); start.ArgumentList.Add(scope);
            Process = System.Diagnostics.Process.Start(start)!;
        }
        internal void Ready()
        {
            var read = Process.StandardOutput.ReadLineAsync();
            if (!read.Wait(TimeSpan.FromSeconds(12)) || read.Result != "READY")
                throw new Exception("Probe did not start: " + (Process.HasExited ? Process.StandardError.ReadToEnd() : "timeout"));
        }
        internal void Stop()
        {
            if (!Process.HasExited) Process.Kill(); // Only this test's own child process.
            Process.WaitForExit(5000);
        }
        public void Dispose() { Stop(); Process.Dispose(); }
    }
}
