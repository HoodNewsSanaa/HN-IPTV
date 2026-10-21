using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Channels;
using HnIptv.Services;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class BufferedTranslationSource:IAsyncDisposable
{
    internal static string TransportRoot=>Path.Combine(AppStorage.DataDirectory,"media-transport","ffmpeg-9.0.2");
    internal static string Executable=>Path.Combine(TransportRoot,"ffmpeg-9.0.2-essentials_build","bin","ffmpeg.exe");
    private static readonly SemaphoreSlim DownloadGate=new(1,1);
    private static int _cleaned;
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(15)};
    private readonly CancellationTokenSource _stop;
    private readonly Channel<CapturedAudio> _audio=Channel.CreateBounded<CapturedAudio>(new BoundedChannelOptions(16){SingleReader=true,SingleWriter=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly Process _process;
    private readonly string _directory;
    private readonly Task _reader,_manifest,_errors,_server;
    private readonly TcpListener _listener=new(IPAddress.Loopback,0);
    private readonly ConcurrentDictionary<TcpClient,Task> _clients=new();
    private readonly string _route;
    private readonly TaskCompletionSource _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _delay;
    private double _head,_translated;
    private volatile bool _ended;
    private int _disposed;
    internal ChannelReader<CapturedAudio> Chunks=>_audio.Reader;
    internal string PlaylistPath=>Path.Combine(_directory,"delayed.m3u8");
    internal Uri PlaybackUri=>new($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{_route}/delayed.m3u8");
    internal double HeadSeconds=>Volatile.Read(ref _head);
    internal BufferedTranslationSource(Uri source,string agent,string referer,int delay,CancellationToken ct,string? executable=null)
    {
        if(delay is not (10 or 15))throw new ArgumentOutOfRangeException(nameof(delay));
        if(agent.Contains('\r')||agent.Contains('\n')||referer.Contains('\r')||referer.Contains('\n'))throw new InvalidDataException("Invalid stream headers.");
        _delay=delay;_stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        CleanupAbandoned();
        _directory=Path.Combine(AppStorage.DataDirectory,"translation-buffer",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory,"session.owner"),"HN-IPTV-buffer-v1");
        _route=Path.GetFileName(_directory);
        var info=new ProcessStartInfo(executable??Executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
        void Add(params string[] args){foreach(string arg in args)info.ArgumentList.Add(arg);}
        Add("-nostdin","-hide_banner","-loglevel","error","-y","-readrate","1","-copyts","-start_at_zero");
        if(source.Scheme is "http" or "https") {Add("-rw_timeout","15000000","-user_agent",string.IsNullOrEmpty(agent)?"HN-IPTV":agent);if(referer.Length>0)Add("-referer",referer);}
        Add("-i",source.IsFile?source.LocalPath:source.AbsoluteUri,
            "-map","0:v:0?","-map","0:a:0","-c","copy","-f","hls","-hls_time","2","-hls_list_size","90",
            "-hls_delete_threshold","30","-hls_flags","delete_segments+temp_file+omit_endlist",
            "-hls_segment_options","mpegts_copyts=1","-hls_segment_filename",Path.Combine(_directory,"segment%09d.ts"),Path.Combine(_directory,"source.m3u8"),
            "-map","0:a:0","-vn","-af","aresample=44100:async=1:first_pts=0","-ac","2","-c:a","pcm_s16le","-f","s16le","pipe:1");
        try{_process=Process.Start(info)??throw new IOException("Could not start the translation buffer.");}
        catch{File.Delete(Path.Combine(_directory,"session.owner"));Directory.Delete(_directory);_stop.Dispose();throw;}
        _errors=Task.Run(async()=>{try{while(await _process.StandardError.ReadLineAsync()!=null){}}catch{}});
        _listener.Start();_server=Task.Run(Serve);
        _reader=Task.Run(ReadAudio);_manifest=Task.Run(UpdateManifest);
    }
    private async Task Serve()
    {
        try {
            while(!_stop.IsCancellationRequested) {
                var client=await _listener.AcceptTcpClientAsync(_stop.Token);
                var task=ServeClient(client);_clients[client]=task;
                _=task.ContinueWith(done=>{_clients.TryRemove(client,out _);client.Dispose();},TaskScheduler.Default);
            }
        }catch(OperationCanceledException){}catch(SocketException) when(_stop.IsCancellationRequested){}catch(ObjectDisposedException) when(_stop.IsCancellationRequested){}
    }
    private async Task ServeClient(TcpClient client)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var ct=timeout.Token;
        try {
            using var stream=client.GetStream();var header=new List<byte>();var one=new byte[1];
            while(header.Count<8192) {
                if(await stream.ReadAsync(one,ct)==0)return;header.Add(one[0]);
                int n=header.Count;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;
            }
            string request=Encoding.ASCII.GetString(header.ToArray());string[] lines=request.Split("\r\n"),first=lines[0].Split(' ');
            if(first.Length!=3||first[0] is not ("GET" or "HEAD"))return;
            string prefix="/"+_route+"/",target=first[1];
            string name=target.StartsWith(prefix,StringComparison.Ordinal)?target[prefix.Length..]:"";
            bool playlist=name=="delayed.m3u8";
            bool segment=System.Text.RegularExpressions.Regex.IsMatch(name,@"\Asegment[0-9]{9}\.ts\z");
            if(!playlist&&!segment)return;
            string path=Path.Combine(_directory,name);
            if(!File.Exists(path)){await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"),ct);return;}
            await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,81920,true);
            long start=0,end=file.Length-1;bool partial=false;
            foreach(string line in lines)if(line.StartsWith("Range: bytes=",StringComparison.OrdinalIgnoreCase)) {
                string[] range=line[13..].Split('-');
                if(range.Length==2&&long.TryParse(range[0],out long offset)&&offset>=0&&offset<file.Length) {
                    start=offset;if(long.TryParse(range[1],out long last)&&last>=start)end=Math.Min(end,last);partial=true;
                }
            }
            long length=end-start+1;
            string response=$"HTTP/1.1 {(partial?"206 Partial Content":"200 OK")}\r\nContent-Type: {(playlist?"application/vnd.apple.mpegurl":"video/mp2t")}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\nCache-Control: no-store\r\nConnection: close\r\n";
            if(partial)response+=$"Content-Range: bytes {start}-{end}/{file.Length}\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response+"\r\n"),ct);
            if(first[0]=="HEAD")return;
            file.Position=start;byte[] bytes=new byte[81920];
            while(length>0){int count=await file.ReadAsync(bytes.AsMemory(0,(int)Math.Min(length,bytes.Length)),ct);if(count==0)break;await stream.WriteAsync(bytes.AsMemory(0,count),ct);length-=count;}
        }catch(IOException){}catch(OperationCanceledException){}catch(SocketException){}catch(ObjectDisposedException){}
    }
    internal static async Task PrepareAsync(Action<string> progress,CancellationToken ct)
    {
        await DownloadGate.WaitAsync(ct);
        try {
            if(File.Exists(Executable)&&File.Exists(Path.Combine(TransportRoot,"verified.ready")))return;
            Directory.CreateDirectory(TransportRoot);string archive=Path.Combine(TransportRoot,"transport.zip.partial");
            try {
                progress("Downloading the FFmpeg media transport once (115 MB); no new language model is needed...");
                using var response=await Http.GetAsync("https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip",HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
                await using var input=await response.Content.ReadAsStreamAsync(ct);
                await using(var output=File.Create(archive)){byte[] buffer=new byte[81920];long size=0;int count;while((count=await input.ReadAsync(buffer,ct))>0){size+=count;if(size>130_000_000)throw new InvalidDataException("Transport download exceeds expected size.");await output.WriteAsync(buffer.AsMemory(0,count),ct);}}
                await PackageIntegrity.VerifyAsync(archive,"60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba",ct);
                ZipFile.ExtractToDirectory(archive,TransportRoot,true);if(!File.Exists(Executable))throw new IOException("Media transport archive is incomplete.");
                await File.WriteAllTextAsync(Path.Combine(TransportRoot,"verified.ready"),"9.0.2",ct);
            }finally{if(File.Exists(archive))File.Delete(archive);}
        }finally{DownloadGate.Release();}
    }
    internal Task WaitReadyAsync(CancellationToken ct)=>_ready.Task.WaitAsync(TimeSpan.FromSeconds(90),ct);
    internal void TranslatedThrough(double seconds){if(double.IsFinite(seconds)&&seconds>Volatile.Read(ref _translated))Volatile.Write(ref _translated,seconds);}
    private async Task ReadAudio()
    {
        try {
            var bytes=new byte[56448];long total=0;
            while(!_stop.IsCancellationRequested) {
                int used=0,count;
                while(used<bytes.Length&&(count=await _process.StandardOutput.BaseStream.ReadAsync(bytes.AsMemory(used),_stop.Token))>0)used+=count;
                if(used==0)break;if(used%4!=0)throw new IOException("Incomplete PCM packet.");
                total+=used;Volatile.Write(ref _head,total/(double)SpeechAudioBuffer.BytesPerSecond);
                await _audio.Writer.WriteAsync(new(bytes.AsSpan(0,used).ToArray(),Stopwatch.GetTimestamp(),0,HeadSeconds),_stop.Token);
            }
            await _process.WaitForExitAsync(_stop.Token);
            if(_process.ExitCode!=0)throw new IOException("The source could not fill the translation buffer.");
            _ended=true;_audio.Writer.TryComplete();
        }catch(OperationCanceledException){_audio.Writer.TryComplete();_ready.TrySetCanceled();}
        catch(Exception ex){_audio.Writer.TryComplete(ex);_ready.TrySetException(ex);}
    }
    private async Task UpdateManifest()
    {
        var playlist=new DelayedPlaylist();string previous="";
        try {
            while(!_stop.IsCancellationRequested) {
                string source=Path.Combine(_directory,"source.m3u8");
                if(File.Exists(source)) {
                    try {
                        playlist.Read(await File.ReadAllTextAsync(source,_stop.Token));
                        string? next=playlist.Build(HeadSeconds,Volatile.Read(ref _translated),_delay,_ended);
                        if(next!=null&&next!=previous) {
                            string path=Path.Combine(_directory,"delayed.m3u8");
                            AtomicFile.Write(path,stream=>{using var writer=new StreamWriter(stream,leaveOpen:true);writer.Write(next);writer.Flush();});
                            previous=next;_ready.TrySetResult();
                        }
                    }catch(IOException){/* Atomic source replacement can briefly deny sharing; retry. */}
                }
                long size=0;foreach(string file in Directory.EnumerateFiles(_directory))try{size+=new FileInfo(file).Length;}catch(FileNotFoundException){}
                if(size>512L*1024*1024)throw new IOException("Translation buffer reached its 512 MB limit.");
                if(_process.HasExited&&!_ended&&_reader.IsFaulted)await _reader;
                await Task.Delay(100,_stop.Token);
            }
        }catch(OperationCanceledException){_ready.TrySetCanceled();}
        catch(Exception ex){_ready.TrySetException(ex);_audio.Writer.TryComplete(ex);}
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        _stop.Cancel();_listener.Stop();foreach(var client in _clients.Keys)client.Dispose();try{if(!_process.HasExited)_process.Kill(true);}catch{}
        try{await Task.WhenAll(_reader,_manifest,_errors,_server).ContinueWith(async _=>await Task.WhenAll(_clients.Values)).Unwrap().WaitAsync(TimeSpan.FromSeconds(5));}catch{}
        _process.Dispose();
        DeleteOwned(_directory);
        _stop.Dispose();
    }
    private static void CleanupAbandoned()
    {
        if(Interlocked.Exchange(ref _cleaned,1)!=0)return;
        string root=Path.Combine(AppStorage.DataDirectory,"translation-buffer");
        if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)==0)
            foreach(string directory in Directory.EnumerateDirectories(root))DeleteOwned(directory);
    }
    private static void DeleteOwned(string directory)
    {
        string root=Path.GetFullPath(Path.Combine(AppStorage.DataDirectory,"translation-buffer"))+Path.DirectorySeparatorChar;
        string target=Path.GetFullPath(directory);
        if(!target.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(target),"N",out _)||!Directory.Exists(target))return;
        try {
            if((File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0||File.ReadAllText(Path.Combine(target,"session.owner"))!="HN-IPTV-buffer-v1")return;
            foreach(string entry in Directory.EnumerateFileSystemEntries(target))if(Directory.Exists(entry)||(File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)return;
            Directory.Delete(target,true);
        }catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
