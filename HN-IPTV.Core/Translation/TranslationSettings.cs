using System.Globalization;
using System.Text.Json;
using HnIptv.Services;
namespace HnIptv.Translation;

public sealed record TranslationSettings
{
    public string InputLanguage { get; init; } = "auto";
    public string OutputLanguage { get; init; } = "auto";
    public string SpeechModel { get; init; } = "auto";
    public string Acceleration { get; init; } = "auto";
    public string TextModel { get; init; } = "auto";
    public string SpeechPipeline { get; init; } = "auto";
    public bool NoiseSuppression { get; init; }
    public string ScreenOverlay { get; init; } = "positioned";
    public string[] InstalledInputs { get; init; } = ["en", "es", "fr", "de", "pt", "it"];
    public string Target => OutputLanguage == "auto" ? NormalizeLanguage(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) : NormalizeLanguage(OutputLanguage);
    public static string NormalizeLanguage(string? value)
    {
        var code = (value ?? "auto").Trim().ToLowerInvariant().Split('-')[0];
        return code is "auto" || (code.Length is 2 or 3 && code.All(char.IsAsciiLetter)) ? code : "auto";
    }
    public TranslationSettings Normalize() => this with {
        InputLanguage = NormalizeLanguage(InputLanguage), OutputLanguage = NormalizeLanguage(OutputLanguage),
        SpeechModel = SpeechModel is "auto" or "tiny" or "base" or "small" or "medium" or "turbo" ? SpeechModel : "auto",
        TextModel = TextModel is "auto" or "argos" ? TextModel : "auto",
        SpeechPipeline = SpeechPipeline is "auto" or "direct" or "staged" ? SpeechPipeline : "auto",
        Acceleration = Acceleration is "auto" or "cpu" or "cuda" ? Acceleration : "auto",
        ScreenOverlay = ScreenOverlay is "positioned" or "captions" ? ScreenOverlay : "positioned",
        InstalledInputs = (InstalledInputs ?? []).Select(NormalizeLanguage).Where(x => x != "auto").Distinct().Take(30).ToArray()
    };
    public static TranslationSettings Load(string path) => File.Exists(path)
        ? (JsonSerializer.Deserialize<TranslationSettings>(File.ReadAllBytes(path)) ?? new()).Normalize() : new();
    public void Save(string path) => AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, Normalize()));
}

public sealed record SpeechCaptionCue(string Text, double DurationSeconds);

public sealed record TranslationResult(string Text, string Source, string Target, string? Error = null, string Backend = "", string? Notice = null,
    IReadOnlyList<ScreenTranslationRegion>? Regions = null, int FrameWidth = 0, int FrameHeight = 0, bool ReplaceSpeech = false, IReadOnlyList<SpeechCaptionCue>? SpeechCues = null);

// Clear all old results at each tune, Stop, settings change, or toggle.
public sealed class TranslationGeneration
{
    private long _value;
    public long Current => Interlocked.Read(ref _value);
    public long Advance() => Interlocked.Increment(ref _value);
    public bool IsCurrent(long value) => Current == value;
}
