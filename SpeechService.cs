using System.Text.RegularExpressions;

namespace OllamaTextLens;

internal enum SpeechPhase { Ready, Downloading, Preparing, Playing, Paused, Stopped, Completed, Failed }
internal sealed record SpeechStatus(SpeechPhase Phase, int Progress=0, Exception? Error=null);
internal interface ISpeechVoice : IDisposable
{
    Task PrepareAsync(IProgress<int> progress,CancellationToken token);
    int SampleRate { get; }
    void Generate(string text,float speed,Action<float[]> receive,CancellationToken token,int speakerId=3);
}
internal interface ISpeechOutput : IDisposable
{
    void Enqueue(float[] samples,CancellationToken token);
    void Pause();
    void Resume();
    void Stop();
    Task DrainAsync(CancellationToken token);
}
internal static class SpeechText
{
    internal static string Normalize(string text)
    {
        text=Regex.Replace(text,@"(?<=\p{L})-\r?\n(?=\p{Ll})","");
        text=Regex.Replace(text,@"[\p{Cc}\p{Cf}]+"," ");
        return Regex.Replace(text,@"\s+"," ").Trim();
    }
    internal static IEnumerable<string> Chunks(string text)
    {
        // Keep punctuation and word order. Bound each native generation to limit stop latency/memory.
        var words=Regex.Matches(text,@"\S+\s*").Select(m=>m.Value);
        string chunk="";
        foreach(var word in words)
        {
            if(chunk.Length>0 && chunk.Length+word.Length>400) { yield return chunk.Trim(); chunk=""; }
            chunk+=word;
            if(chunk.Length>=80 && Regex.IsMatch(word.TrimEnd(),@"[.!?;]$")) { yield return chunk.Trim(); chunk=""; }
        }
        if(chunk.Length>0) yield return chunk.Trim();
    }
}

internal sealed class SpeechService : IDisposable
{
    readonly ISpeechVoice voice;
    readonly Func<int,ISpeechOutput> outputFactory;
    readonly SemaphoreSlim generation=new(1,1);
    readonly object sync=new();
    CancellationTokenSource? current;
    ISpeechOutput? output;
    bool disposed;
    SpeechStatus status=new(SpeechPhase.Ready);
    internal event Action? Changed;
    internal SpeechStatus Status { get { lock(sync) return status; } }
    internal SpeechService(ISpeechVoice voice,Func<int,ISpeechOutput> outputFactory)
    { this.voice=voice; this.outputFactory=outputFactory; }

    void Publish(CancellationTokenSource session,SpeechStatus value)
    {
        lock(sync) { if(current!=session || session.IsCancellationRequested) return; status=value; }
        Changed?.Invoke();
    }
    internal async Task PlayAsync(string text,float speed,int speakerId=3)
    {
        Stop();
        var session=new CancellationTokenSource();
        lock(sync)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            current=session; status=new(SpeechPhase.Preparing);
        }
        Changed?.Invoke();
        ISpeechOutput? activeOutput=null;
        bool acquired=false;
        try
        {
            text=SpeechText.Normalize(text);
            if(!Regex.IsMatch(text,"[A-Za-z]")) throw new AppError("SpeechEnglishOnly");
            if(text.Length>12000) throw new AppError("TextTooLong");
            if(!float.IsFinite(speed) || speed<0.5f || speed>2f) throw new ArgumentOutOfRangeException(nameof(speed));
            if(speakerId is not (3 or 16)) throw new ArgumentOutOfRangeException(nameof(speakerId));
            await generation.WaitAsync(session.Token); acquired=true;
            await Task.Run(async()=> {
                await voice.PrepareAsync(new DirectProgress(p=>Publish(session,new(SpeechPhase.Downloading,p))),session.Token);
                session.Token.ThrowIfCancellationRequested();
                Publish(session,new(SpeechPhase.Preparing));
                activeOutput=outputFactory(voice.SampleRate);
                lock(sync) { session.Token.ThrowIfCancellationRequested(); output=activeOutput; }
                bool started=false;
                foreach(var chunk in SpeechText.Chunks(text))
                {
                    session.Token.ThrowIfCancellationRequested();
                    voice.Generate(chunk,speed,samples=> {
                        activeOutput.Enqueue(samples,session.Token);
                        if(!started) { started=true; Publish(session,new(SpeechPhase.Playing)); }
                    },session.Token,speakerId);
                }
                if(!started) throw new AppError("SpeechNoAudio");
                await activeOutput.DrainAsync(session.Token);
            },session.Token);
            Publish(session,new(SpeechPhase.Completed));
        }
        catch(OperationCanceledException) when(session.IsCancellationRequested) { }
        catch(Exception error) { Publish(session,new(SpeechPhase.Failed,Error:error)); }
        finally
        {
            lock(sync) { if(current==session) { current=null; output=null; } }
            activeOutput?.Dispose();
            if(acquired) generation.Release();
            session.Dispose();
        }
    }
    internal void TogglePause()
    {
        lock(sync)
        {
            if(output==null) return;
            if(status.Phase==SpeechPhase.Playing) { output.Pause(); status=new(SpeechPhase.Paused); }
            else if(status.Phase==SpeechPhase.Paused) { output.Resume(); status=new(SpeechPhase.Playing); }
            else return;
        }
        Changed?.Invoke();
    }
    internal void Stop()
    {
        lock(sync)
        {
            current?.Cancel(); current=null;
            output?.Stop(); output=null;
            status=new(SpeechPhase.Stopped);
        }
        Changed?.Invoke();
    }
    public void Dispose()
    {
        lock(sync) { if(disposed) return; disposed=true; }
        Stop();
        // Do not free native model memory until any cancelled synthesis has returned.
        _=Task.Run(async()=> { await generation.WaitAsync(); try { voice.Dispose(); } finally { generation.Release(); } });
    }
    sealed class DirectProgress(Action<int> report) : IProgress<int> { public void Report(int value)=>report(value); }
}
