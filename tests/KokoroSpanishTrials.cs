using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using OllamaTextLens;
using SherpaOnnx;

internal static class KokoroSpanishTrials
{
    internal static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var folder=SpeechModel.DirectoryPath;
        if(!SpeechModel.IsReady(folder)) throw new Exception("Install the existing Kokoro model first.");
        if(!File.Exists(Path.Combine(folder,"espeak-ng-data","es_dict"))) throw new Exception("Spanish pronunciation data missing.");
        var config=new OfflineTtsConfig();
        config.Model.Kokoro.Model=Path.Combine(folder,"model.int8.onnx");
        config.Model.Kokoro.Voices=Path.Combine(folder,"voices.bin");
        config.Model.Kokoro.Tokens=Path.Combine(folder,"tokens.txt");
        config.Model.Kokoro.DataDir=Path.Combine(folder,"espeak-ng-data");
        // Spanish uses the bundled espeak frontend, not the English lexicon.
        config.Model.Kokoro.Lexicon="";
        config.Model.Kokoro.Lang="es";
        config.Model.Provider="cpu";
        config.Model.NumThreads=Math.Clamp(Environment.ProcessorCount/2,1,4);
        config.MaxNumSentences=1;
        var load=Stopwatch.StartNew();
        using var engine=new OfflineTts(config);
        load.Stop();
        if(engine.NumSpeakers<=29 || engine.SampleRate!=24000) throw new Exception("Unexpected Kokoro model.");
        const string paragraph="Hola, esta es una prueba de lectura en español. Selecciona un fragmento de texto y escucha su pronunciación natural, sin enviar el contenido a un servicio de pago.";
        const string shortText="Hola, ¿cómo estás? Esta es una prueba de lectura en español.";
        var cases=new[] {
            (Name:"spanish-short-female", Text:shortText, Speaker:28, Voice:"Dora", Speed:1f),
            (Name:"spanish-female", Text:paragraph, Speaker:28, Voice:"Dora", Speed:1f),
            (Name:"spanish-male", Text:paragraph, Speaker:29, Voice:"Alex", Speed:1f),
            (Name:"spanish-female-fast", Text:paragraph, Speaker:28, Voice:"Dora", Speed:1.25f)
        };
        var results=new List<Trial>();
        foreach(var item in cases)
        {
            var samples=new List<float>();
            double? firstAudio=null;
            Exception? callbackError=null;
            var watch=Stopwatch.StartNew();
            OfflineTtsCallback callback=(pointer,count)=> {
                try {
                    if(count>0) {
                        firstAudio??=watch.Elapsed.TotalSeconds;
                        var block=new float[count]; Marshal.Copy(pointer,block,0,count); samples.AddRange(block);
                    }
                    return 1;
                } catch(Exception e) { callbackError=e; return 0; }
            };
            foreach(var chunk in SpeechText.Chunks(item.Text))
            {
                var audio=engine.GenerateWithCallback(chunk,item.Speed,item.Speaker,callback);
                try { if(callbackError!=null) throw callbackError; }
                finally { audio.Dispose(); }
            }
            GC.KeepAlive(callback);
            watch.Stop();
            if(samples.Count<engine.SampleRate || samples.Any(x=>!float.IsFinite(x))) throw new Exception("Invalid audio.");
            var rms=Math.Sqrt(samples.Select(x=>(double)x*x).Average());
            if(rms<0.001 || firstAudio==null) throw new Exception("Silent audio.");
            using(var wave=new WaveFileWriter(Path.Combine(outputDirectory,item.Name+".wav"),new WaveFormat(engine.SampleRate,16,1)))
                wave.WriteSamples(samples.ToArray(),0,samples.Count);
            var result=new Trial(item.Name,item.Text,item.Voice,item.Speed,samples.Count/(double)engine.SampleRate,watch.Elapsed.TotalSeconds,firstAudio.Value,rms);
            results.Add(result);
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        File.WriteAllText(Path.Combine(outputDirectory,"results.json"),JsonSerializer.Serialize(new { Model="kokoro-int8-multi-lang-v1_0",Runtime="sherpa-onnx 1.12.17",Language="es",Provider="CPU",Threads=config.Model.NumThreads,LoadSeconds=load.Elapsed.TotalSeconds,Results=results },new JsonSerializerOptions { WriteIndented=true }));
        var html=new StringBuilder("<!doctype html><html lang=\"zh-Hant\"><meta charset=\"utf-8\"><title>Kokoro 西班牙文試聽</title><style>body{font:18px system-ui;max-width:850px;margin:40px auto;padding:0 20px;background:#faf8f1;color:#242424}section{padding:20px;background:white;margin:20px 0;border-radius:12px}audio{width:100%}h2{font-size:22px}</style><h1>Kokoro 西班牙文試聽</h1><p>本機 CPU、西班牙文發音設定。尚未整合 App，無須另外下載模型。</p>");
        html.Append($"<p>模型首次載入：{load.Elapsed.TotalSeconds:F2} 秒。首段時間只計算音訊生成，不含音效裝置緩衝與載入模型。這是生成基準測試，並未實測喇叭首次出聲或連續播放。</p>");
        foreach(var r in results) {
            html.Append($"<section><h2>{r.Voice} · {r.Speed:F2}× · {WebUtility.HtmlEncode(r.Name)}</h2><p>{WebUtility.HtmlEncode(r.Text)}</p><audio controls preload=\"none\" src=\"{r.Name}.wav\"></audio><p>首段音訊就緒 {r.FirstAudioSeconds:F2} 秒；整段生成 {r.GenerationSeconds:F2} 秒；音訊長度 {r.AudioSeconds:F2} 秒。</p></section>");
        }
        html.Append("<section><h2>先前 Qwen3-TTS 西班牙文女聲對照</h2><p>相同段落，Serena，原先生成約 16.4 秒。</p><audio controls preload=\"none\" src=\"../qwen3/spanish-female.wav\"></audio></section><p>音檔已檢查可讀且非靜音；發音、重音與自然度請實際試聽判斷。</p></html>");
        File.WriteAllText(Path.Combine(outputDirectory,"listen.html"),html.ToString(),Encoding.UTF8);
        Console.WriteLine($"DONE: {Path.GetFullPath(outputDirectory)}; load {load.Elapsed.TotalSeconds:F2}s");
    }
    private sealed record Trial(string Name,string Text,string Voice,float Speed,double AudioSeconds,double GenerationSeconds,double FirstAudioSeconds,double Rms);
}
