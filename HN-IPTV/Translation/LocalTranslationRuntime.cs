using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using HnIptv.Services;
using HnIptv.Translation;
namespace HnIptv;

internal static class LocalTranslationRuntime
{
    internal static string Root=>Path.Combine(AppStorage.DataDirectory,"speech-whisper");
    internal static string Model(TranslationSettings settings)=>Path.Combine(Root,settings.UsesLocalTranslation?"ggml-medium-q5_0.bin":"ggml-large-v3-turbo-q5_0.bin");
    private static string Marker(TranslationSettings settings)=>Path.Combine(Root,settings.UsesLocalTranslation?"ready-medium-v1.json":"ready-v1.json");
    internal static string VadModel=>Path.Combine(Root,"ggml-silero-v6.2.0.bin");
    internal static string Worker=>Path.Combine(Path.GetDirectoryName(typeof(LocalTranslationRuntime).Assembly.Location)!,"HN-IPTV.SpeechWorker.exe");
    internal static bool IsReady(TranslationSettings settings)=>File.Exists(Model(settings))&&File.Exists(VadModel)&&File.Exists(Marker(settings));
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(30)};
    private static readonly SemaphoreSlim SetupGate=new(1,1);
    private const string Hash="394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2";
    internal static string Runtime(string backend)=>Path.GetDirectoryName(Worker)!;
    internal static string PreparedBackend(TranslationSettings settings) {try{using var d=JsonDocument.Parse(File.ReadAllText(Marker(settings)));return d.RootElement.GetProperty("backend").GetString()=="vulkan"?"vulkan":"cpu";}catch{return "cpu";}}
    internal static async Task InstallAsync(TranslationSettings settings,Action<string> progress,CancellationToken ct)
    {
        await SetupGate.WaitAsync(ct);
        try {
            Directory.CreateDirectory(Root);
            string model=Model(settings),hash=settings.UsesLocalTranslation?"19fea4b380c3a618ec4723c3eef2eb785ffba0d0538cf43f8f235e7b3b34220f":Hash;
            if(!File.Exists(model)) {
                progress(settings.UsesLocalTranslation?"Downloading Whisper Medium Q5 for on-device English translation (539 MB)...":"Downloading multilingual Whisper large-v3-turbo Q5 (574 MB)...");
                string url=settings.UsesLocalTranslation?"https://huggingface.co/ggerganov/whisper.cpp/resolve/f281eb45af861ab5e5297d23694b7d46e090c02c/ggml-medium-q5_0.bin":"https://huggingface.co/ggerganov/whisper.cpp/resolve/98aa99a0a9db05ae2342309f5096248665f7cba3/ggml-large-v3-turbo-q5_0.bin";
                await Download(url,model,hash,600_000_000,progress,ct);
            }else await PackageIntegrity.VerifyAsync(model,hash,ct);
            const string vadHash="2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987";
            if(!File.Exists(VadModel)) {
                progress("Downloading local speech-boundary detector (0.9 MB)...");
                await Download("https://huggingface.co/ggml-org/whisper-vad/resolve/9ffd54a1e1ee413ddf265af9913beaf518d1639b/ggml-silero-v6.2.0.bin",VadModel,vadHash,1_000_000,progress,ct);
            }else await PackageIntegrity.VerifyAsync(VadModel,vadHash,ct);
            string backend=settings.Acceleration=="cpu"?"cpu":"vulkan";
            progress("Validating Whisper model and "+backend+" inference...");
            try{await Validate(backend,settings,ct);}
            catch(OperationCanceledException){throw;}
            catch when(backend=="vulkan"&&settings.Acceleration=="auto"){progress("GPU unavailable; validating CPU fallback. Buffered playback may refill more often on CPU.");backend="cpu";await Validate(backend,settings,ct);}
            await File.WriteAllTextAsync(Marker(settings),JsonSerializer.Serialize(new{backend,model_sha256=hash}),ct);
            progress(settings.UsesLocalTranslation?"Whisper ready. English translation stays on this device; no Azure key or translation connection is needed.":"Whisper ready. Azure translates recognized text; audio stays local.");
        }finally{SetupGate.Release();}
    }
    private static async Task Validate(string backend,TranslationSettings settings,CancellationToken ct)
    {
        await using var worker=new LocalTranslationWorker(settings,backend);await worker.PrepareAsync(ct);
        // A nonzero, quiet tone forces actual encoder/decoder inference without uploading user audio.
        var probe=new byte[176400];
        for(int i=0;i<44100;i++){short sample=(short)(Math.Sin(i*2*Math.PI*440/44100)*200);BitConverter.TryWriteBytes(probe.AsSpan(i*4,2),sample);BitConverter.TryWriteBytes(probe.AsSpan(i*4+2,2),sample);}
        await worker.RecognizeAsync(probe,ct);await worker.FinishAsync(ct);
    }
    private static async Task Download(string url,string path,string hash,long maximum,Action<string> progress,CancellationToken ct)
    {
        string temporary=path+".partial";
        try {
            using var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            await using var input=await response.Content.ReadAsStreamAsync(ct);
            await using(var output=File.Create(temporary)) {
                byte[] buffer=new byte[81920];long size=0,last=0;int count;
                while((count=await input.ReadAsync(buffer,ct))!=0){size+=count;if(size>maximum)throw new InvalidDataException("Download exceeds expected size.");await output.WriteAsync(buffer.AsMemory(0,count),ct);if(size-last>=50_000_000){progress($"Downloaded {size/1_000_000} MB...");last=size;}}
            }
            await PackageIntegrity.VerifyAsync(temporary,hash,ct);File.Move(temporary,path,true);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }

}

internal sealed record RecognitionText(string Text,string Language,bool Final,double StartSeconds,double EndSeconds);
internal sealed class LocalTranslationWorker:IAsyncDisposable
{
    private readonly Process _process;private readonly Task _errors;private readonly SemaphoreSlim _gate=new(1,1);
    private long _sequence;private bool _ready;private volatile bool _faulted;
    internal string Backend{get;}
    internal bool CanReuse{get{try{return !_faulted&&_gate.CurrentCount==1&&!_process.HasExited;}catch{return false;}}}
    internal LocalTranslationWorker(TranslationSettings settings,string? backend=null)
    {
        Backend=backend??(settings.Acceleration is "cpu" or "vulkan"?settings.Acceleration:LocalTranslationRuntime.PreparedBackend(settings));
        var info=new ProcessStartInfo(LocalTranslationRuntime.Worker){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};
        foreach(string arg in new[]{LocalTranslationRuntime.Runtime(Backend),LocalTranslationRuntime.Model(settings),settings.InputLanguage,Backend,settings.BufferSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),settings.UsesLocalTranslation?"translate":"transcribe"})info.ArgumentList.Add(arg);
        _process=Process.Start(info)??throw new IOException("Could not start Whisper.");
        _errors=Task.Run(async()=>{try{while(await _process.StandardError.ReadLineAsync()!=null){}}catch{}});
    }
    internal async Task PrepareAsync(CancellationToken ct)
    {
        if(_ready)return;await _gate.WaitAsync(ct);
        try {
            if(_ready)return;
            string line=await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromMinutes(2),ct)??throw new IOException("Whisper stopped during startup.");
            using var json=JsonDocument.Parse(line);if(!json.RootElement.GetProperty("ready").GetBoolean())throw new IOException("Whisper could not load. Prepare the runtime/model or choose CPU.");_ready=true;
        }catch{_faulted=true;throw;}finally{_gate.Release();}
    }
    internal Task<IReadOnlyList<RecognitionText>> RecognizeAsync(byte[] pcm,CancellationToken ct)=>Request("audio",pcm,ct);
    internal Task<IReadOnlyList<RecognitionText>> FinishAsync(CancellationToken ct)=>Request("finish",[],ct);
    internal async Task ResetAsync(CancellationToken ct)=>_=await Request("reset",[],ct);
    private async Task<IReadOnlyList<RecognitionText>> Request(string kind,byte[] data,CancellationToken ct)
    {
        await PrepareAsync(ct);await _gate.WaitAsync(ct);
        try {
            long id=++_sequence;await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{id,kind,data=Convert.ToBase64String(data)}).AsMemory(),ct);await _process.StandardInput.FlushAsync(ct);
            string response=await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromMinutes(2),ct)??throw new IOException("Whisper stopped.");
            using var doc=JsonDocument.Parse(response);var root=doc.RootElement;
            if(root.GetProperty("id").GetInt64()!=id||root.TryGetProperty("error",out _))throw new IOException("Whisper recognition failed. Prepare the model or choose CPU.");
            return root.GetProperty("results").EnumerateArray().Select(x=>new RecognitionText(x.GetProperty("Text").GetString()??"",x.GetProperty("Language").GetString()??"auto",x.GetProperty("Final").GetBoolean(),x.GetProperty("StartSeconds").GetDouble(),x.GetProperty("EndSeconds").GetDouble())).ToArray();
        }catch{_faulted=true;throw;}finally{_gate.Release();}
    }
    public async ValueTask DisposeAsync(){try{if(!_process.HasExited)_process.Kill(true);}catch{}try{await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));await _errors;}catch{}_process.Dispose();}
}
