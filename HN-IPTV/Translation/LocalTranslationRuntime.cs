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
    internal static string Root=>Path.Combine(AppStorage.DataDirectory,"speech-nemotron");
    internal static string Models=>Path.Combine(Root,"model.gguf");
    internal static string Worker=>Path.Combine(Path.GetDirectoryName(typeof(LocalTranslationRuntime).Assembly.Location)!,"HN-IPTV.SpeechWorker.exe");
    internal static bool Ready=>File.Exists(Models)&&File.Exists(Path.Combine(Root,"ready-v1.json"));
    private static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(30)};
    private static readonly SemaphoreSlim SetupGate=new(1,1);
    private const string Hash="a5c435f294eea8f88ce68dd27b8c3bfea7f777cb2fbba04fcd30eaa555f429ae";
    internal static string Runtime(string backend)=>Path.Combine(Root,"runtime-"+backend,"nemo-speech-0.2.0-windows-x86_64-"+backend,"bin");
    internal static string PreparedBackend {get {try{using var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"ready-v1.json")));return d.RootElement.GetProperty("backend").GetString()=="cuda"?"cuda":"cpu";}catch{return "cpu";}}}
    internal static async Task InstallAsync(TranslationSettings settings,Action<string> progress,CancellationToken ct)
    {
        await SetupGate.WaitAsync(ct);
        try {
            Directory.CreateDirectory(Root);
            bool gpu=settings.Acceleration=="cuda"||settings.Acceleration=="auto"&&await HasGpu(ct);
            await PrepareRuntime("cpu",progress,ct);if(gpu)await PrepareRuntime("cuda",progress,ct);
            if(!File.Exists(Models)) {
                progress("Downloading one multilingual Nemotron model (742 MB)...");
                await Download("https://huggingface.co/nvidia/nemotron-3.5-asr-streaming-0.6b/resolve/1c8deaecc64b91f034d73e08dd8b64625eb3395d/nemotron-3.5-asr-streaming-0.6b.q8_0.gguf",Models,Hash,800_000_000,progress,ct);
            }else await PackageIntegrity.VerifyAsync(Models,Hash,ct);
            progress("Validating native model loading and streaming inference...");
            string backend=gpu?"cuda":"cpu";
            try{await Validate(backend,settings,ct);}
            catch(OperationCanceledException){throw;}
            catch when(gpu&&settings.Acceleration=="auto"){progress("GPU unavailable; validating native CPU fallback.");backend="cpu";await Validate(backend,settings,ct);}
            await File.WriteAllTextAsync(Path.Combine(Root,"ready-v1.json"),JsonSerializer.Serialize(new{backend,model_sha256=Hash}),ct);
            await Task.Run(()=>RemoveLegacy(progress),ct);
            progress("Nemotron ready. Recognition stays local; Azure translates recognized text.");
        }finally{SetupGate.Release();}
    }
    private static async Task Validate(string backend,TranslationSettings settings,CancellationToken ct)
    {
        await using var worker=new LocalTranslationWorker(settings,backend);await worker.PrepareAsync(ct);
        await worker.RecognizeAsync(new byte[176400],ct);await worker.FinishAsync(ct);
    }
    private static async Task PrepareRuntime(string backend,Action<string> progress,CancellationToken ct)
    {
        if(File.Exists(Path.Combine(Runtime(backend),"nemo_speech_asr_c.dll")))return;
        progress("Downloading native "+backend+" runtime...");string archive=Path.Combine(Root,backend+".zip");
        string hash=backend=="cuda"?"9ffe5e842c39a7d3211f3b2c3b3aa1bca8c0b2455d96d961ed5f22ec5108f98d":"82c80451086e86194aba9af19f800da77cd66d1c28d993d0efceffd6cdb49bb8";
        await Download("https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v0.2.0/nemo-speech-0.2.0-windows-x86_64-"+backend+".zip",archive,hash,150_000_000,progress,ct);
        ZipFile.ExtractToDirectory(archive,Path.Combine(Root,"runtime-"+backend),true);File.Delete(archive);
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
    internal static void RemoveLegacy(Action<string> progress)
    {
        if(!Ready)return;
        string data=Path.GetFullPath(AppStorage.DataDirectory).TrimEnd(Path.DirectorySeparatorChar);
        string legacy=Path.GetFullPath(Path.Combine(data,"translation"));
        if(!legacy.StartsWith(data+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(legacy)!="translation")throw new IOException("Invalid legacy directory.");
        if(!Directory.Exists(legacy))return;
        void Check(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked legacy folder.");if(Directory.Exists(path))foreach(string child in Directory.EnumerateFileSystemEntries(path))Check(child);}
        try{Check(legacy);Directory.Delete(legacy,true);progress("Removed old Python, Whisper, OCR, translation packs and their CUDA libraries.");}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){progress("Some old language files are in use or linked. Close other HN-IPTV copies and run setup again to retry cleanup.");}
    }
    private static async Task<bool> HasGpu(CancellationToken ct)
    {
        try {
            var info=new ProcessStartInfo("nvidia-smi.exe"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
            info.ArgumentList.Add("--query-gpu=name");info.ArgumentList.Add("--format=csv,noheader");using var p=Process.Start(info);if(p==null)return false;
            var output=p.StandardOutput.ReadToEndAsync(ct);var errors=p.StandardError.ReadToEndAsync(ct);
            try{await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(5),ct);await errors;return p.ExitCode==0&&!string.IsNullOrWhiteSpace(await output);}finally{if(!p.HasExited)p.Kill(true);}
        }catch(OperationCanceledException){throw;}catch{return false;}
    }
}

internal sealed record RecognitionText(string Text,string Language,bool Final);
internal sealed class LocalTranslationWorker:IAsyncDisposable
{
    private readonly Process _process;private readonly Task _errors;private readonly SemaphoreSlim _gate=new(1,1);
    private long _sequence;private bool _ready;private volatile bool _faulted;
    internal string Backend{get;}
    internal bool CanReuse{get{try{return !_faulted&&_gate.CurrentCount==1&&!_process.HasExited;}catch{return false;}}}
    internal LocalTranslationWorker(TranslationSettings settings,string? backend=null)
    {
        Backend=backend??(settings.Acceleration=="cpu"?"cpu":LocalTranslationRuntime.PreparedBackend);
        var info=new ProcessStartInfo(LocalTranslationRuntime.Worker){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true};
        foreach(string arg in new[]{LocalTranslationRuntime.Runtime(Backend),LocalTranslationRuntime.Models,settings.InputLanguage,Backend})info.ArgumentList.Add(arg);
        _process=Process.Start(info)??throw new IOException("Could not start Nemotron.");
        _errors=Task.Run(async()=>{try{while(await _process.StandardError.ReadLineAsync()!=null){}}catch{}});
    }
    internal async Task PrepareAsync(CancellationToken ct)
    {
        if(_ready)return;await _gate.WaitAsync(ct);
        try {
            if(_ready)return;
            string line=await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromMinutes(2),ct)??throw new IOException("Nemotron stopped during startup.");
            using var json=JsonDocument.Parse(line);if(!json.RootElement.GetProperty("ready").GetBoolean())throw new IOException("Nemotron could not load. Prepare the runtime/model or choose CPU.");_ready=true;
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
            string response=await _process.StandardOutput.ReadLineAsync(ct).AsTask().WaitAsync(TimeSpan.FromSeconds(30),ct)??throw new IOException("Nemotron stopped.");
            using var doc=JsonDocument.Parse(response);var root=doc.RootElement;
            if(root.GetProperty("id").GetInt64()!=id||root.TryGetProperty("error",out _))throw new IOException("Nemotron recognition failed. Prepare the model or choose CPU.");
            return root.GetProperty("results").EnumerateArray().Select(x=>new RecognitionText(x.GetProperty("Text").GetString()??"",x.GetProperty("Language").GetString()??"auto",x.GetProperty("Final").GetBoolean())).ToArray();
        }catch{_faulted=true;throw;}finally{_gate.Release();}
    }
    public async ValueTask DisposeAsync(){try{if(!_process.HasExited)_process.Kill(true);}catch{}try{await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));await _errors;}catch{}_process.Dispose();}
}
