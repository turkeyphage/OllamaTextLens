using System.Diagnostics;
using System.Text.Json;
using NAudio.Wave;
using OllamaTextLens;

internal static class JapaneseSpeechTests
{
    static void Check(bool value,string message) { if(!value) throw new Exception(message); Console.WriteLine("PASS "+message); }
    static async Task Until(Func<bool> condition,int timeout=40000) {
        var watch=Stopwatch.StartNew();
        while(!condition()) { if(watch.ElapsedMilliseconds>timeout) throw new TimeoutException("Japanese playback timed out"); await Task.Delay(25); }
    }
    internal static async Task Run(string directory,bool playback=true)
    {
        var executable=Path.Combine(Path.GetFullPath(directory),"speech-ja","JapaneseSpeech","JapaneseSpeech.exe");
        var output=Path.Combine(directory,"kokoro-japanese"); Directory.CreateDirectory(output);
        const string sample="こんにちは。これは日本語の読み上げテストです。文章を選択すると、自然な声で原文を聞くことができます。";
        using(var missing=new JapaneseSpeech(Path.Combine(output,"missing.exe"))) {
            bool failed=false; try { await missing.PrepareAsync(SpeechModel.DirectoryPath,CancellationToken.None); } catch(AppError e) when(e.Key=="SpeechJapaneseMissing") { failed=true; }
            Check(failed,"missing Japanese runtime fails without a download or English fallback");
        }
        var rows=new List<object>();
        using(var voice=new KokoroVoice(japaneseExecutable:executable)) {
            var load=Stopwatch.StartNew();
            await voice.PrepareAsync(new Progress<int>(_=>{}),CancellationToken.None,"ja");
            Console.WriteLine($"Japanese helper load {load.Elapsed.TotalSeconds:F2}s");
            foreach(var (speaker,speed) in new[]{(37,1f),(41,1f),(37,1.25f)}) {
                var audio=new List<float>(); var watch=Stopwatch.StartNew(); double? first=null;
                voice.Generate(sample,speed,x=> { first??=watch.Elapsed.TotalSeconds; audio.AddRange(x); },CancellationToken.None,speaker);
                watch.Stop();
                Check(audio.Count>24000 && audio.All(float.IsFinite) && audio.Any(x=>Math.Abs(x)>0.01),$"packaged Japanese voice {speaker} at {speed}x is audible");
                using var file=new WaveFileWriter(Path.Combine(output,$"integrated-ja-{speaker}-{speed.ToString("0.##",System.Globalization.CultureInfo.InvariantCulture)}.wav"),new WaveFormat(24000,16,1));
                file.WriteSamples(audio.ToArray(),0,audio.Count);
                rows.Add(new { speaker,speed,first_audio_seconds=first,generation_seconds=watch.Elapsed.TotalSeconds,audio_seconds=audio.Count/24000d });
            }
            await voice.PrepareAsync(new Progress<int>(_=>{}),CancellationToken.None,"en");
            int count=0; voice.Generate("This is an English voice after reading Japanese.",1f,x=>count+=x.Length,CancellationToken.None,3);
            Check(count>24000,"Japanese-to-English model switch works");
            await voice.PrepareAsync(new Progress<int>(_=>{}),CancellationToken.None,"ja");
            count=0; voice.Generate("日本語",1f,x=>count+=x.Length,CancellationToken.None,41);
            Check(count>0,"English-to-Japanese switch and kanji-only manual reading work");
        }
        File.WriteAllText(Path.Combine(output,"integration-results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions { WriteIndented=true }));
        if(!playback) return; // GitHub runners have no interactive audio device.
        SpeechOutput? device=null;
        using var service=new SpeechService(new KokoroVoice(japaneseExecutable:executable),rate=>device=new SpeechOutput(rate));
        var playing=service.PlayAsync(sample,1f,37,"auto");
        await Until(()=>service.Status.Phase is SpeechPhase.Playing or SpeechPhase.Failed);
        if(service.Status.Phase==SpeechPhase.Failed) throw service.Status.Error!;
        await Until(()=>device!.PlayedBytes>4000);
        service.TogglePause(); await Task.Delay(250); var position=device!.PlayedBytes; await Task.Delay(350);
        Check(service.Status.Phase==SpeechPhase.Paused && device.PlayedBytes==position,"real Japanese audio pauses");
        service.TogglePause(); await Until(()=>device.PlayedBytes>position);
        Check(service.Status.Phase==SpeechPhase.Playing,"real Japanese audio resumes");
        var stop=Stopwatch.StartNew(); service.Stop(); await playing.WaitAsync(TimeSpan.FromSeconds(10));
        Check(service.Status.Phase==SpeechPhase.Stopped,"Japanese stop cancels helper and playback");
        Console.WriteLine($"Japanese stop returned in {stop.Elapsed.TotalSeconds:F2}s");
        var restarted=service.PlayAsync("こんにちは。今日はいい天気ですね。",1.25f,41,"ja");
        await restarted.WaitAsync(TimeSpan.FromSeconds(40));
        if(service.Status.Phase==SpeechPhase.Failed) throw service.Status.Error!;
        Check(service.Status.Phase==SpeechPhase.Completed,"Japanese male reading restarts and drains after stop");
    }
}
