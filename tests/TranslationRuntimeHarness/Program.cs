using System.Reflection;
using System.IO;
using System.Runtime.Loader;
using System.Text.Json;
using HnIptv.Translation;

if (args.Length != 3) throw new ArgumentException("Expected published assembly, synthetic data directory and report path.");
var published = Path.GetFullPath(args[0]);
var runtimeFiles = Path.Combine(Path.GetDirectoryName(published)!, "TranslationRuntime");
foreach (var source in Directory.GetFiles(runtimeFiles, "*", SearchOption.AllDirectories)) {
    var destination = Path.Combine(AppContext.BaseDirectory, "TranslationRuntime", Path.GetRelativePath(runtimeFiles, source));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
    File.Copy(source, destination, true);
}
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(published);
var storage = assembly.GetType("HnIptv.AppStorage", true)!;
storage.GetProperty("TestDataDirectory", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, Path.GetFullPath(args[1]));
storage.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null);
var runtime = assembly.GetType("HnIptv.LocalTranslationRuntime", true)!;
var settings = new TranslationSettings { SpeechModel = "tiny", Acceleration = "cpu", InstalledInputs = ["en", "es"], OutputLanguage = "es" };
var install = (Task)runtime.GetMethod("InstallAsync", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [settings, (Action<string>)Console.WriteLine, CancellationToken.None])!;
await install;
// Exercise the BPE Spanish-to-English pack as well as SentencePiece English-to-Spanish.
await (Task)runtime.GetMethod("InstallAsync", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [settings with { OutputLanguage = "en" }, (Action<string>)Console.WriteLine, CancellationToken.None])!;
await (Task)runtime.GetMethod("InstallAsync", BindingFlags.Static | BindingFlags.NonPublic)!
    .Invoke(null, [settings with { InstalledInputs = ["ar"], OutputLanguage = "en" }, (Action<string>)Console.WriteLine, CancellationToken.None])!;
string python = (string)runtime.GetProperty("Python", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
string models = (string)runtime.GetProperty("Models", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
File.WriteAllText(args[2], JsonSerializer.Serialize(new { python, models, passed = true }));
Console.WriteLine("The same private-runtime setup used by the app completed.");
