using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HnIptv.Models;

public sealed class Channel : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Number { get; init; }
    public string GroupId { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string Logo { get; init; } = "";
    public string EpgId { get; init; } = "";
    public string Command { get; init; } = "";

    private string _logoImage = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string LogoImage
    {
        get => _logoImage;
        set { if (_logoImage != value) { _logoImage = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasLogo)); OnPropertyChanged(nameof(HasNoLogo)); } }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLogo => !string.IsNullOrEmpty(LogoImage);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasNoLogo => !HasLogo;
    private bool _isFavorite;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFavorite
    {
        get => _isFavorite;
        set { if (_isFavorite != value) { _isFavorite = value; OnPropertyChanged(); OnPropertyChanged(nameof(FavoriteIcon)); } }
    }
    private bool _isVisible = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
    }

    private string _epgPreview = "No EPG information";
    [System.Text.Json.Serialization.JsonIgnore]
    public string EpgPreview
    {
        get => _epgPreview;
        private set { if (_epgPreview != value) { _epgPreview = value; OnPropertyChanged(); } }
    }
    private string _epgAirtime = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string EpgAirtime
    {
        get => _epgAirtime;
        private set { if (_epgAirtime != value) { _epgAirtime = value; OnPropertyChanged(); } }
    }
    private double _epgProgress;
    [System.Text.Json.Serialization.JsonIgnore]
    public double EpgProgress
    {
        get => _epgProgress;
        private set { if (_epgProgress != value) { _epgProgress = value; OnPropertyChanged(); } }
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EpgPreviewLoaded { get; set; }

    public void SetEpgPreview(IReadOnlyList<ProgramEntry> programs, DateTimeOffset now)
    {
        var current = programs.FirstOrDefault(p => p.Start <= now && p.End > now);
        if (current != null)
        {
            EpgPreview = "Now: " + current.Title;
            EpgAirtime = current.TimeLabel;
            var length = (current.End!.Value - current.Start!.Value).TotalSeconds;
            EpgProgress = length > 0 ? Math.Clamp(100 * (now - current.Start.Value).TotalSeconds / length, 0, 100) : 0;
            return;
        }
        var next = programs.Where(p => p.Start > now).OrderBy(p => p.Start).FirstOrDefault();
        EpgPreview = next is null ? "No EPG information" : "Next: " + next.Title;
        EpgAirtime = next?.TimeLabel ?? "";
        EpgProgress = 0;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string FavoriteIcon => IsFavorite ? "★" : "☆";
    [System.Text.Json.Serialization.JsonIgnore]
    public string NumberLabel => Number > 0 ? Number.ToString("000") : "TV";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record ChannelGroup(string Id, string Name);

public sealed record ProgramEntry(string Title, string Description, DateTimeOffset? Start, DateTimeOffset? End)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string TimeLabel => Start is not null && End is not null
        ? $"{Start.Value:HH:mm}–{End.Value:HH:mm}"
        : "Schedule";
}
