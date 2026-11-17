using System.Diagnostics;
using NAudio.Wave;
using OllamaTextLens;

internal static class SpeechTests
{
    static void Check(bool value,string message)
    { if(!value) throw new Exception(message); Console.WriteLine("PASS "+message); }
    static async Task Until(Func<bool> condition,int milliseconds=5000)
    {
        var watch=Stopwatch.StartNew();
        while(!condition()) { if(watch.ElapsedMilliseconds>milliseconds) throw new TimeoutException("Speech state timed out"); await Task.Delay(15); }
    }
    internal static async Task Run()
    {
        await CheckModelAvailability();
        Check(SpeechText.Normalize("An inter-\nnational study.\r\nNext line.")=="An international study. Next line.","speech repairs PDF line breaks without rewriting the source");
        var longText=string.Join(" ",Enumerable.Repeat("This paragraph keeps its punctuation, dates, and original wording.",30));
        var chunks=SpeechText.Chunks(longText).ToArray();
        Check(chunks.All(c=>c.Length<=400) && string.Join(" ",chunks)==longText,"speech chunking preserves all text and bounds generation size");
        var voice=new FakeVoice(); FakeOutput? device=null;
        using var speech=new SpeechService(voice,_=>device=new FakeOutput());
        var playing=speech.PlayAsync("A readable English sentence.",1.25f);
        await Until(()=>speech.Status.Phase==SpeechPhase.Playing);
        Check(voice.Speed==1.25f,"selected speed reaches the speech model");
        Check(voice.Speaker==3,"female voice is the default");
        speech.TogglePause();
        Check(speech.Status.Phase==SpeechPhase.Paused && device!.Paused,"pause reaches the playback device");
        speech.TogglePause();
        Check(speech.Status.Phase==SpeechPhase.Playing && !device!.Paused,"resume keeps the existing playback session");
        speech.Stop(); await playing.WaitAsync(TimeSpan.FromSeconds(5));
        Check(speech.Status.Phase==SpeechPhase.Stopped && device!.Stopped,"stop cancels generation and discards playback");
        var old=speech.PlayAsync("The old selection.",1f);
        await Until(()=>speech.Status.Phase==SpeechPhase.Playing);
        var latest=speech.PlayAsync("The new selection.",0.75f,16);
        await Until(()=>voice.LastText=="The new selection." && speech.Status.Phase==SpeechPhase.Playing);
        speech.Stop(); await Task.WhenAll(old,latest).WaitAsync(TimeSpan.FromSeconds(5));
        Check(voice.MaxConcurrent==1,"replacement selection never synthesizes two voices concurrently");
        Check(voice.Speaker==16,"male voice selection reaches the speech model");
        await speech.PlayAsync("你好",1f);
        Check(speech.Status.Phase==SpeechPhase.Failed && speech.Status.Error is AppError e && e.Key=="SpeechEnglishOnly","unsupported non-English selection gives a readable error");
        await speech.PlayAsync("Some English text.",float.NaN);
        Check(speech.Status.Phase==SpeechPhase.Failed,"invalid speech speed is rejected safely");
    }
    static async Task CheckModelAvailability()
    {
        var folder=Path.Combine(Path.GetTempPath(),"OllamaTextLens-model-check-"+Guid.NewGuid().ToString("N"));
        Check(SpeechModel.Inspect(folder)==SpeechModelState.Missing,"missing speech model disables readiness");
        using(var absentVoice=new KokoroVoice(folder))
        {
            bool rejected=false;
            try { await absentVoice.PrepareAsync(new Progress<int>(_=>throw new Exception("Unexpected download")),CancellationToken.None); }
            catch(AppError error) when(error.Key=="SpeechModelMissing") { rejected=true; }
            Check(rejected && !Directory.Exists(folder),"playback without an installed model fails safely without downloading");
        }
        Directory.CreateDirectory(folder);
        try
        {
            Check(SpeechModel.Inspect(folder)==SpeechModelState.Incomplete,"empty model folder is not an installation");
            foreach(var (file,length) in new[]{("model.int8.onnx",114202993L),("voices.bin",28200960L)})
            { using var stream=File.Create(Path.Combine(folder,file)); stream.SetLength(length); }
            Directory.CreateDirectory(Path.Combine(folder,"espeak-ng-data"));
            Directory.CreateDirectory(Path.Combine(folder,"espeak-ng-data/lang/gmw"));
            Check(!SpeechModel.IsReady(folder),"missing phonemization and lexicon data disable speech");
            foreach(var file in new[]{"tokens.txt","lexicon-us-en.txt","espeak-ng-data/phondata","espeak-ng-data/phonindex","espeak-ng-data/phontab","espeak-ng-data/en_dict","espeak-ng-data/lang/gmw/en-US"})
                File.WriteAllText(Path.Combine(folder,file),"fixture");
            Check(SpeechModel.IsReady(folder),"complete model file inventory enables readiness");
            File.WriteAllBytes(Path.Combine(folder,"voices.bin"),[]);
            Check(!SpeechModel.IsReady(folder),"truncated voice table disables speech again");
        }
        finally { Directory.Delete(folder,true); }
    }
    internal static async Task RunLive(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Check(SpeechModel.IsReady(SpeechModel.DirectoryPath),"real Kokoro model installed locally");
        using var voice=new KokoroVoice();
        var watch=Stopwatch.StartNew();
        await voice.PrepareAsync(new Progress<int>(p=>Console.WriteLine("Model "+p+"%")),CancellationToken.None);
        Check(voice.SampleRate==24000,"real Kokoro output is 24 kHz");
        const string sample="Welcome to OllamaTextLens. Select a passage and listen to the original English text, spoken naturally on your own computer. You can pause, continue, stop, or choose a different reading speed.";
        var durations=new List<double>();
        foreach(var (speed,name) in new[]{(1f,"speech-normal.wav"),(0.75f,"speech-slow.wav"),(1.5f,"speech-fast.wav")})
        {
            var collected=new List<float>();
            voice.Generate(sample,speed,samples=>collected.AddRange(samples),CancellationToken.None);
            Check(collected.Count>voice.SampleRate && collected.All(float.IsFinite),"real synthesis produces valid audio at "+speed+"x");
            double rms=Math.Sqrt(collected.Select(x=>(double)x*x).Average());
            Check(rms>0.001,"generated speech is not silence at "+speed+"x");
            using(var wave=new WaveFileWriter(Path.Combine(outputDirectory,name),WaveFormat.CreateIeeeFloatWaveFormat(voice.SampleRate,1)))
                wave.WriteSamples(collected.ToArray(),0,collected.Count);
            durations.Add(collected.Count/(double)voice.SampleRate);
            Console.WriteLine($"WAV {name}: {durations[^1]:F2}s, RMS {rms:F4}");
        }
        Check(durations[1]>durations[0] && durations[2]<durations[0],"model changes speed rather than just changing a UI label");
        var maleSamples=new List<float>();
        voice.Generate(sample,1f,samples=>maleSamples.AddRange(samples),CancellationToken.None,16);
        Check(maleSamples.Count>24000 && maleSamples.All(float.IsFinite),"real Michael male voice generates valid speech");
        Check(!maleSamples.Take(24000).SequenceEqual(ReadFirstSecond(Path.Combine(outputDirectory,"speech-normal.wav"))),"male and female voices produce distinct waveforms");
        using(var wave=new WaveFileWriter(Path.Combine(outputDirectory,"speech-male.wav"),WaveFormat.CreateIeeeFloatWaveFormat(voice.SampleRate,1)))
            wave.WriteSamples(maleSamples.ToArray(),0,maleSamples.Count);
        Console.WriteLine($"Real synthesis and sample export: {watch.Elapsed.TotalSeconds:F1}s");

        SpeechOutput? audio=null;
        using var player=new SpeechService(new KokoroVoice(),rate=>audio=new SpeechOutput(rate));
        var playback=player.PlayAsync(string.Join(" ",Enumerable.Repeat(sample,3)),1f);
        await Until(()=>player.Status.Phase is SpeechPhase.Playing or SpeechPhase.Failed,60000);
        if(player.Status.Phase==SpeechPhase.Failed) throw player.Status.Error!;
        await Until(()=>audio!.PlayedBytes>4000);
        player.TogglePause(); await Task.Delay(250);
        long pausedAt=audio!.PlayedBytes; await Task.Delay(400);
        Check(player.Status.Phase==SpeechPhase.Paused && audio.PlayedBytes==pausedAt,"real audio device stops advancing while paused");
        player.TogglePause(); await Until(()=>audio.PlayedBytes>pausedAt);
        Check(player.Status.Phase==SpeechPhase.Playing,"real audio resumes from the same position");
        player.Stop(); await playback.WaitAsync(TimeSpan.FromSeconds(20));
        Check(player.Status.Phase==SpeechPhase.Stopped,"real playback and synthesis stop cleanly");

        var completed=player.PlayAsync("This is an offline voice test. The reading is now complete.",1.5f);
        await completed.WaitAsync(TimeSpan.FromSeconds(60));
        Check(player.Status.Phase==SpeechPhase.Completed,"real playback drains completely and reports finished");
    }
    sealed class FakeVoice : ISpeechVoice
    {
        public int SampleRate=>24000;
        internal float Speed;
        internal int Speaker;
        internal string LastText="";
        int concurrent;
        internal int MaxConcurrent;
        public Task PrepareAsync(IProgress<int> progress,CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public void Generate(string text,float speed,Action<float[]> receive,CancellationToken token,int speakerId=3)
        {
            int active=Interlocked.Increment(ref concurrent); MaxConcurrent=Math.Max(MaxConcurrent,active);
            try
            {
                Speed=speed; Speaker=speakerId; LastText=text; receive(new float[2400]);
                token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested();
            }
            finally { Interlocked.Decrement(ref concurrent); }
        }
        public void Dispose() { }
    }
    static float[] ReadFirstSecond(string file)
    {
        using var reader=new AudioFileReader(file);
        var samples=new float[24000]; int count=reader.Read(samples,0,samples.Length);
        return samples.Take(count).ToArray();
    }
    sealed class FakeOutput : ISpeechOutput
    {
        internal bool Paused,Stopped;
        public void Enqueue(float[] samples,CancellationToken token)=>token.ThrowIfCancellationRequested();
        public void Pause()=>Paused=true;
        public void Resume()=>Paused=false;
        public void Stop()=>Stopped=true;
        public Task DrainAsync(CancellationToken token)=>Task.CompletedTask;
        public void Dispose()=>Stop();
    }
}
