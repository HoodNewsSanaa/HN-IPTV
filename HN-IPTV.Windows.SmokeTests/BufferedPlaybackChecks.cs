using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using HnIptv;
using HnIptv.Playback;

internal static class BufferedPlaybackChecks
{
    internal static async Task RunAsync(MainWindow window,NativeVideoHost host,PlaybackController controller,Dictionary<string,object> report,string directory)
    {
        await (Task)typeof(MainWindow).GetMethod("StopPlaybackAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null)!;
        await BufferedTranslationSource.PrepareAsync(Console.WriteLine,CancellationToken.None);
        string fixture=Path.Combine(directory,"buffer-fixture.ts");
        var info=new ProcessStartInfo(BufferedTranslationSource.Executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true};
        foreach(string arg in new[]{"-hide_banner","-loglevel","error","-y","-f","lavfi","-i","color=c=red:s=160x90:r=10:d=8","-f","lavfi","-i","color=c=blue:s=160x90:r=10:d=14","-f","lavfi","-i","sine=frequency=440:sample_rate=44100:duration=8","-f","lavfi","-i","sine=frequency=880:sample_rate=44100:duration=14","-filter_complex","[0:v][1:v]concat=n=2:v=1:a=0[v];[2:a][3:a]concat=n=2:v=0:a=1[a]","-map","[v]","-map","[a]","-c:v","libx264","-preset","ultrafast","-g","10","-bf","0","-c:a","aac","-muxdelay","0","-muxpreload","0","-f","mpegts",fixture})info.ArgumentList.Add(arg);
        using(var p=Process.Start(info)!){string error=await p.StandardError.ReadToEndAsync();await p.WaitForExitAsync();if(p.ExitCode!=0)throw new Exception("Buffer fixture generation failed: "+error);}
        var bufferMode=typeof(PlaybackController).GetMethod("TranslationBufferAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(70));
        await using(var source=new BufferedTranslationSource(new Uri(fixture),"","",10,cancel.Token)) {
            string session=Path.GetDirectoryName(source.PlaylistPath)!;
            var ready=source.WaitReadyAsync(cancel.Token);var reached=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool translated=false;using var pcm=new MemoryStream();
            var consume=Task.Run(async()=>{
                await foreach(var chunk in source.Chunks.ReadAllAsync(cancel.Token)) {
                    if(pcm.Length<176400*12)pcm.Write(chunk.Wave);
                    if(source.HeadSeconds>=12)reached.TrySetResult();
                    if(Volatile.Read(ref translated))source.TranslatedThrough(chunk.AudioEndSeconds);
                }
            });
            await reached.Task.WaitAsync(cancel.Token);
            if(ready.IsCompleted)throw new Exception("Video was exposed before translations were ready.");
            Volatile.Write(ref translated,true);source.TranslatedThrough(source.HeadSeconds);
            await ready;
            Console.WriteLine("BUFFER-READY head="+source.HeadSeconds+" manifest="+File.ReadAllText(source.PlaylistPath));
            if(source.HeadSeconds<12)throw new Exception("Generous mode did not accumulate its lead time.");
            await controller.StopAsync();await (Task)typeof(PlaybackController).GetMethod("TraceForTestAsync",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(controller,[(Action<string>)Console.WriteLine])!;await (Task)bufferMode.Invoke(controller,[true])!;
            await controller.PlayAsync(source.PlaybackUri,host.VideoHandle,0,cancel.Token);
            await Until(async()=>{var p=await controller.ProgressAsync();return p.Playing&&p.Time>=1000;},cancel.Token);
            var initial=await controller.ProgressAsync();
            if(initial.Time>3500)throw new Exception("Delayed player jumped to the live edge or used the wrong timestamp origin: "+initial.Time);
            await FrameColor(controller,true);
            double clock=initial.Time/1000d;
            var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};
            timer.Tick+=async(_,_)=>clock=(await controller.ProgressAsync()).Time/1000d;timer.Start();
            var shared=new HnIptv.Translation.TimedCaptions();
            var overlay=new TranslationOverlay(()=>(window,host)){SpeechPlaybackClock=()=>clock,SpeechSchedule=shared};
            try {
                overlay.ScheduleSpeech("A timestamped caption",2,4);
                await Until(()=>Task.FromResult(overlay.SpeechCaptionVisible),cancel.Token);
                await controller.PauseAsync();var paused=await controller.ProgressAsync();await Task.Delay(1300,cancel.Token);
                if(Math.Abs((await controller.ProgressAsync()).Time-paused.Time)>120||!overlay.SpeechCaptionVisible)throw new Exception("Pause changed the media/caption clock.");
                overlay.Close();overlay=new TranslationOverlay(()=>(window,host)){SpeechPlaybackClock=()=>clock,SpeechSchedule=shared};
                await Until(()=>Task.FromResult(overlay.SpeechCaptionVisible),cancel.Token);
                report["generous_caption_overlay_recreation"]=true;
                await controller.PauseAsync();await Until(()=>Task.FromResult(!overlay.SpeechCaptionVisible),cancel.Token);
            }finally{timer.Stop();overlay.Close();}
            await Until(async()=>(await controller.ProgressAsync()).Time>=8300,cancel.Token);
            await FrameColor(controller,false);
            await consume;
            byte[] audio=pcm.ToArray();
            double Frequency(int second) {
                int changes=0;short before=0;int left=second*176400,right=Math.Min(audio.Length,left+176400);
                for(int at=left;at+3<right;at+=4){short sample=BitConverter.ToInt16(audio,at);if(before<=0&&sample>0)changes++;before=sample;}return changes;
            }
            if(Math.Abs(Frequency(2)-440)>6||Math.Abs(Frequency(9)-880)>6)throw new Exception("Recognition audio did not share the generated media timeline.");
            report["generous_buffer_audio_video_timestamps"]=true;report["generous_buffer_translation_gate"]=true;
            report["generous_buffer_caption_pause_clock"]=true;
            await controller.StopAsync();await (Task)bufferMode.Invoke(controller,[false])!;
            await source.DisposeAsync();if(Directory.Exists(session))throw new Exception("Stopped buffer retained its temporary media files.");
        }
        using var aborted=new CancellationTokenSource();
        await using(var source=new BufferedTranslationSource(new Uri(fixture),"","",15,aborted.Token)) {
            var ready=source.WaitReadyAsync(aborted.Token);aborted.Cancel();
            try{await ready;throw new Exception("Canceled buffer reported ready.");}catch(OperationCanceledException){}
        }
        report["generous_buffer_startup_cancellation"]=true;
    }
    private static async Task FrameColor(PlaybackController controller,bool red)
    {
        var method=typeof(PlaybackController).GetMethod("CaptureVideoFrameAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
        byte[] bytes=await (Task<byte[]?>)method.Invoke(controller,[CancellationToken.None])!??throw new Exception("No buffered video frame.");
        using var stream=new MemoryStream(bytes);var image=BitmapDecoder.Create(stream,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
        var converted=new FormatConvertedBitmap(image,System.Windows.Media.PixelFormats.Bgra32,null,0);byte[] pixels=new byte[converted.PixelWidth*converted.PixelHeight*4];converted.CopyPixels(pixels,converted.PixelWidth*4,0);
        long r=0,b=0;for(int i=0;i<pixels.Length;i+=4){b+=pixels[i];r+=pixels[i+2];}
        if(red?r<=b*2:b<=r*2)throw new Exception($"Buffered video disagreed with its source timestamp: expectedRed={red}, red={r}, blue={b}, time={(await controller.ProgressAsync()).Time}.");
    }
    private static async Task Until(Func<Task<bool>> condition,CancellationToken ct)
    {for(int i=0;i<300;i++){if(await condition())return;await Task.Delay(100,ct);}throw new TimeoutException("Buffered playback check timed out.");}
}
