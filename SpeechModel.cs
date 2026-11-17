using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using SherpaOnnx;

namespace OllamaTextLens;

internal enum SpeechModelState { Missing, Incomplete, Ready }
internal static class SpeechModel
{
    internal const string Name="kokoro-int8-multi-lang-v1_0";
    internal const string Sha256="72c347230107ef8a31098ec4d468a605244beef5f1ed00db97efa13518800653";
    internal const string Url="https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/"+Name+".tar.bz2";
    internal static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OllamaTextLens","speech");
    internal static string DirectoryPath=>Path.Combine(Root,Name);
    static readonly SemaphoreSlim install=new(1,1);
    internal static SpeechModelState Inspect(string directory)
    {
        if(!Directory.Exists(directory)) return SpeechModelState.Missing;
        try
        {
            // Sizes are from the pinned, checksum-verified model archive. Reject partial/empty files.
            foreach(var (name,size) in new[]{("model.int8.onnx",114202993L),("voices.bin",28200960L)})
            {
                var file=new FileInfo(Path.Combine(directory,name));
                if(!file.Exists || file.Length!=size) return SpeechModelState.Incomplete;
            }
            foreach(var name in new[]{"tokens.txt","lexicon-us-en.txt","espeak-ng-data/phondata","espeak-ng-data/phonindex","espeak-ng-data/phontab","espeak-ng-data/en_dict","espeak-ng-data/lang/gmw/en-US"})
            {
                var file=new FileInfo(Path.Combine(directory,name));
                if(!file.Exists || file.Length==0) return SpeechModelState.Incomplete;
            }
            return SpeechModelState.Ready;
        }
        catch(IOException) { return SpeechModelState.Incomplete; }
        catch(UnauthorizedAccessException) { return SpeechModelState.Incomplete; }
    }
    internal static bool IsReady(string directory)=>Inspect(directory)==SpeechModelState.Ready;
    internal static async Task EnsureAsync(IProgress<int> progress,CancellationToken token)
    {
        if(IsReady(DirectoryPath)) return;
        await install.WaitAsync(token);
        string? staging=null;
        try
        {
            if(IsReady(DirectoryPath)) return;
            Directory.CreateDirectory(Root);
            staging=Path.Combine(Root,".install-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            string archive=Path.Combine(staging,"model.tar.bz2");
            using(var client=new HttpClient { Timeout=TimeSpan.FromMinutes(20) })
            using(var response=await client.GetAsync(Url,HttpCompletionOption.ResponseHeadersRead,token))
            {
                response.EnsureSuccessStatusCode();
                await using var source=await response.Content.ReadAsStreamAsync(token);
                await using var destination=File.Create(archive);
                byte[] buffer=new byte[128*1024]; long received=0;
                int last=-1,read;
                progress.Report(0);
                while((read=await source.ReadAsync(buffer,token))>0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0,read),token); received+=read;
                    int percent=(int)Math.Min(99,received*100/(response.Content.Headers.ContentLength??132353410));
                    if(percent!=last) { last=percent; progress.Report(percent); }
                }
            }
            await using(var file=File.OpenRead(archive))
            {
                var digest=Convert.ToHexString(await SHA256.HashDataAsync(file,token));
                if(!digest.Equals(Sha256,StringComparison.OrdinalIgnoreCase)) throw new AppError("SpeechChecksumFailed");
            }
            // Only the pinned, hash-verified upstream archive is ever passed to the OS extractor.
            var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"tar.exe"))
                { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
            foreach(var argument in new[]{"-xf",archive,"-C",staging}) start.ArgumentList.Add(argument);
            using(var process=Process.Start(start)??throw new AppError("SpeechInstallFailed"))
            {
                var error=process.StandardError.ReadToEndAsync(token);
                var output=process.StandardOutput.ReadToEndAsync(token);
                try { await process.WaitForExitAsync(token); await Task.WhenAll(error,output); }
                catch { if(!process.HasExited) { process.Kill(); await process.WaitForExitAsync(CancellationToken.None); } throw; }
                if(process.ExitCode!=0) throw new AppError("SpeechInstallFailed");
            }
            token.ThrowIfCancellationRequested();
            var extracted=Path.Combine(staging,Name);
            if(!IsReady(extracted)) throw new AppError("SpeechInstallFailed");
            // Never delete an existing model directory. Preserve incomplete installs for diagnosis.
            if(Directory.Exists(DirectoryPath)) Directory.Move(DirectoryPath,Path.Combine(Root,Name+".incomplete-"+Guid.NewGuid().ToString("N")));
            Directory.Move(extracted,DirectoryPath);
            progress.Report(100);
        }
        finally
        {
            if(staging!=null && Directory.Exists(staging))
                try { Directory.Delete(staging,true); } catch(IOException) { } catch(UnauthorizedAccessException) { }
            install.Release();
        }
    }
}

internal sealed class KokoroVoice(string? modelDirectory=null) : ISpeechVoice
{
    OfflineTts? engine;
    public int SampleRate=>engine?.SampleRate??24000;
    public Task PrepareAsync(IProgress<int> progress,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Playback never installs or downloads. Installation is an explicit settings action.
        var folder=modelDirectory??SpeechModel.DirectoryPath;
        if(!SpeechModel.IsReady(folder)) throw new AppError("SpeechModelMissing");
        if(engine!=null) return Task.CompletedTask;
        var config=new OfflineTtsConfig();
        config.Model.Kokoro.Model=Path.Combine(folder,"model.int8.onnx");
        config.Model.Kokoro.Voices=Path.Combine(folder,"voices.bin");
        config.Model.Kokoro.Tokens=Path.Combine(folder,"tokens.txt");
        config.Model.Kokoro.DataDir=Path.Combine(folder,"espeak-ng-data");
        config.Model.Kokoro.Lexicon=Path.Combine(folder,"lexicon-us-en.txt");
        config.Model.Kokoro.Lang="en-us";
        config.Model.NumThreads=Math.Clamp(Environment.ProcessorCount/2,1,4);
        config.Model.Provider="cpu";
        config.MaxNumSentences=1;
        engine=new OfflineTts(config);
        if(engine.SampleRate<=0 || engine.NumSpeakers<=16) throw new AppError("SpeechModelFailed");
        return Task.CompletedTask;
    }
    public void Generate(string text,float speed,Action<float[]> receive,CancellationToken token,int speakerId=3)
    {
        token.ThrowIfCancellationRequested();
        if(speakerId is not (3 or 16)) throw new ArgumentOutOfRangeException(nameof(speakerId));
        Exception? callbackError=null;
        OfflineTtsCallback callback=(pointer,count)=> {
            if(token.IsCancellationRequested) return 0;
            try
            {
                if(count>0) { var samples=new float[count]; Marshal.Copy(pointer,samples,0,count); receive(samples); }
                return token.IsCancellationRequested?0:1;
            }
            catch(Exception error) { callbackError=error; return 0; }
        };
        // Exceptions must never unwind through the native callback boundary.
        // Stable Kokoro v1.0 mapping: 3 = af_heart, 16 = am_michael (both US English).
        var audio=engine!.GenerateWithCallback(text,speed,speakerId,callback);
        try
        {
            token.ThrowIfCancellationRequested();
            if(callbackError!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(callbackError).Throw();
        }
        finally { audio.Dispose(); GC.KeepAlive(callback); }
    }
    public void Dispose() { engine?.Dispose(); engine=null; }
}
