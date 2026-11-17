using NAudio.Wave;

namespace OllamaTextLens;

internal sealed class SpeechOutput : ISpeechOutput
{
    readonly WaveOutEvent device=new() { DesiredLatency=150 };
    readonly BufferedWaveProvider buffer;
    readonly object sync=new();
    bool paused,started,stopped;
    Exception? deviceError;
    internal long PlayedBytes { get { lock(sync) return device.GetPosition(); } }
    internal SpeechOutput(int sampleRate)
    {
        buffer=new BufferedWaveProvider(new WaveFormat(sampleRate,16,1)) { BufferDuration=TimeSpan.FromSeconds(30),ReadFully=true };
        device.Init(buffer);
        device.PlaybackStopped+=(_,e)=> { if(e.Exception!=null) deviceError=e.Exception; };
    }
    public void Enqueue(float[] samples,CancellationToken token)
    {
        // Short writes keep Pause/Stop responsive even when synthesis is ahead of playback.
        for(int offset=0;offset<samples.Length;)
        {
            token.ThrowIfCancellationRequested();
            if(deviceError!=null) throw deviceError;
            int count=Math.Min(4096,samples.Length-offset);
            if(buffer.BufferedBytes+count*2>buffer.BufferLength)
            { if(token.WaitHandle.WaitOne(25)) token.ThrowIfCancellationRequested(); continue; }
            var pcm=new byte[count*2];
            for(int i=0;i<count;i++)
            {
                short sample=(short)(Math.Clamp(samples[offset+i],-1f,1f)*32767);
                pcm[i*2]=(byte)sample; pcm[i*2+1]=(byte)(sample>>8);
            }
            lock(sync)
            {
                token.ThrowIfCancellationRequested();
                if(stopped) throw new OperationCanceledException(token);
                buffer.AddSamples(pcm,0,pcm.Length);
                if(!started) { started=true; if(!paused) device.Play(); }
            }
            offset+=count;
        }
    }
    public void Pause() { lock(sync) { if(stopped) return; paused=true; device.Pause(); } }
    public void Resume() { lock(sync) { if(stopped) return; paused=false; if(started) device.Play(); } }
    public void Stop() { lock(sync) { stopped=true; device.Stop(); buffer.ClearBuffer(); } }
    public async Task DrainAsync(CancellationToken token)
    {
        while(true)
        {
            token.ThrowIfCancellationRequested();
            if(deviceError!=null) throw deviceError;
            bool pending; lock(sync) pending=paused || buffer.BufferedBytes>0;
            if(!pending)
            {
                await Task.Delay(220,token); // Allow the native device's final queued buffers to play.
                lock(sync) pending=paused || buffer.BufferedBytes>0;
                if(!pending) return;
            }
            await Task.Delay(30,token);
        }
    }
    public void Dispose() { Stop(); device.Dispose(); }
}
