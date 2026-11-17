using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace OllamaTextLens;

internal sealed class JapaneseSpeech(string? executable=null) : IDisposable
{
    internal static string Executable=>Path.Combine(AppContext.BaseDirectory,"speech-ja","JapaneseSpeech","JapaneseSpeech.exe");
    static bool IsInstalled(string path)=>File.Exists(path) && Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!,"_internal","unidic_lite","dicdir"));
    internal static bool IsAvailable=>IsInstalled(Executable);
    Process? process;
    internal async Task PrepareAsync(string folder,CancellationToken token)
    {
        var path=executable??Executable;
        if(!IsInstalled(path)) throw new AppError("SpeechJapaneseMissing");
        if(process is { HasExited:false }) return;
        Dispose();
        var start=new ProcessStartInfo(path) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8 };
        start.ArgumentList.Add(folder);
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        var child=process=Process.Start(start)??throw new AppError("SpeechModelFailed");
        _=child.StandardError.ReadToEndAsync(); // Drain diagnostics; never save source text or audio.
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var cancel=timeout.Token.Register(()=>Kill(child));
        try {
            var line=await child.StandardOutput.ReadLineAsync(timeout.Token);
            using var ready=JsonDocument.Parse(line??"{}");
            if(!ready.RootElement.TryGetProperty("ready",out var rate) || rate.GetInt32()!=24000) throw new AppError("SpeechModelFailed");
        } catch { Dispose(); throw; }
    }
    internal void Generate(string text,float speed,int speaker,Action<float[]> receive,CancellationToken token)
    {
        var child=process??throw new AppError("SpeechModelFailed");
        using var cancel=token.Register(()=>Kill(child));
        try {
            child.StandardInput.WriteLine(JsonSerializer.Serialize(new { text,speed,speaker }));
            child.StandardInput.Flush();
            while(true) {
                token.ThrowIfCancellationRequested();
                var line=child.StandardOutput.ReadLineAsync(token).AsTask().GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                if(line==null) throw new AppError("SpeechModelFailed");
                using var message=JsonDocument.Parse(line);
                if(message.RootElement.TryGetProperty("done",out _)) return;
                if(!message.RootElement.TryGetProperty("audio",out var audio)) throw new AppError("SpeechModelFailed");
                var bytes=Convert.FromBase64String(audio.GetString()!);
                if(bytes.Length%4!=0 || bytes.Length>24000*60*4) throw new AppError("SpeechNoAudio");
                var samples=new float[bytes.Length/4]; Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);
                if(samples.Any(x=>!float.IsFinite(x))) throw new AppError("SpeechNoAudio");
                receive(samples);
            }
        } catch { Kill(child); token.ThrowIfCancellationRequested(); throw; }
    }
    static void Kill(Process child) { try { if(!child.HasExited) child.Kill(true); } catch(InvalidOperationException) { } }
    public void Dispose() { if(process is {} child) { process=null; Kill(child); child.Dispose(); } }
}
