using System.Reflection;
using System.IO;
using System.Runtime.Loader;
using System.Text.Json;
using HnIptv.Translation;

if(args.Length!=4)throw new ArgumentException("Expected published assembly, isolated data, report and PCM WAV fixture.");
var published=Path.GetFullPath(args[0]);var data=Path.GetFullPath(args[1]);Directory.CreateDirectory(data);
var assembly=AssemblyLoadContext.Default.LoadFromAssemblyPath(published);
var storage=assembly.GetType("HnIptv.AppStorage",true)!;
storage.GetProperty("TestDataDirectory",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,data);
storage.GetMethod("Initialize",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,null);
var runtime=assembly.GetType("HnIptv.LocalTranslationRuntime",true)!;
var settings=new TranslationSettings{InputLanguage="en",OutputLanguage="es",Acceleration="cpu"};
string legacy=Path.Combine(data,"translation");Directory.CreateDirectory(legacy);File.WriteAllText(Path.Combine(legacy,"legacy-test-model"),"fake old model");
string sentinel=Path.Combine(data,"playlist-sentinel");File.WriteAllText(sentinel,"keep playlists and keys outside legacy directory");
await (Task)runtime.GetMethod("InstallAsync",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[settings,(Action<string>)Console.WriteLine,CancellationToken.None])!;
if(Directory.Exists(legacy)||!File.Exists(sentinel))throw new Exception("Migration did not safely remove only old language downloads.");
var type=assembly.GetType("HnIptv.LocalTranslationWorker",true)!;
var worker=(IAsyncDisposable)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,[settings,null],null)!;
var prepare=type.GetMethod("PrepareAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
var recognize=type.GetMethod("RecognizeAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
var finish=type.GetMethod("FinishAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
var reset=type.GetMethod("ResetAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
var texts=new List<string>();
async Task Results(Task task){await task;var value=task.GetType().GetProperty("Result")!.GetValue(task);foreach(var item in (System.Collections.IEnumerable)value!)texts.Add((string)item.GetType().GetProperty("Text")!.GetValue(item)!);}
try {
    await (Task)prepare.Invoke(worker,[CancellationToken.None])!;
    // WAV generated below is mono PCM16 at 44.1 kHz. Locate the data chunk rather than assuming 44-byte header.
    using var reader=new BinaryReader(File.OpenRead(args[3]));reader.BaseStream.Position=12;byte[] pcm=[];int rate=0,channels=0,bits=0;
    while(reader.BaseStream.Position+8<=reader.BaseStream.Length){string id=new string(reader.ReadChars(4));int size=reader.ReadInt32();long end=reader.BaseStream.Position+size;if(id=="fmt "){if(reader.ReadInt16()!=1)throw new Exception("PCM fixture required");channels=reader.ReadInt16();rate=reader.ReadInt32();reader.ReadInt32();reader.ReadInt16();bits=reader.ReadInt16();}if(id=="data")pcm=reader.ReadBytes(size);reader.BaseStream.Position=end+(size%2);}
    if(rate!=44100||channels!=1||bits!=16)throw new Exception("Unexpected fixture format.");
    byte[] stereo=new byte[pcm.Length*2];for(int i=0;i<pcm.Length/2;i++){stereo[i*4]=stereo[i*4+2]=pcm[i*2];stereo[i*4+1]=stereo[i*4+3]=pcm[i*2+1];}
    for(int offset=0;offset<stereo.Length;offset+=56448)await Results((Task)recognize.Invoke(worker,[stereo.AsSpan(offset,Math.Min(56448,stereo.Length-offset)).ToArray(),CancellationToken.None])!);
    await Results((Task)finish.Invoke(worker,[CancellationToken.None])!);
    if(!texts.Any(x=>x.Contains("hello",StringComparison.OrdinalIgnoreCase)))throw new Exception("Native worker did not recognize spoken fixture.");
    await (Task)reset.Invoke(worker,[CancellationToken.None])!;texts.Clear();
    await Results((Task)recognize.Invoke(worker,[new byte[176400],CancellationToken.None])!);
    await Results((Task)finish.Invoke(worker,[CancellationToken.None])!);
    if(texts.Any(x=>!string.IsNullOrWhiteSpace(x)))throw new Exception("Old channel words leaked into silence after reset.");
}finally{await worker.DisposeAsync();}
File.WriteAllText(args[2],JsonSerializer.Serialize(new{passed=true,native_streaming=true,safe_migration=true,model="Nemotron 3.5 Q8",backend="cpu"}));
Console.WriteLine("Actual native worker speech, reset, silence and migration checks passed.");
