using System.Collections.ObjectModel;
using System.Windows.Threading;
using HnIptv.Preferences;
using HnIptv.Playback;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Diagnostics;
using HnIptv.Models;
using HnIptv.Navigation;
using HnIptv.Services;
using Microsoft.Win32;

namespace HnIptv;

public partial class MainWindow : Window
{
    // Each provider supplies a snapshot. Swapping it atomically avoids ListBox/CollectionView
    // reentrancy during thousands of ObservableCollection.Reset/Add events.
    private List<Channel> _channels = new();
    private ChannelIndex _channelIndex = new(Array.Empty<Channel>());
    private readonly ObservableCollection<ProgramEntry> _programs = new();
    private ICollectionView _channelView;
    private readonly BulkObservableCollection<Channel> _visibleChannels = new();
    private string _boundGroup = "";
    private readonly Dictionary<string, double> _groupOffsets = new();
    private PlayerSettings _playerSettings = new();
    private PictureInPictureWindow? _pip;
    private readonly DispatcherTimer _infoTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _volumeDebounce = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _readingInfo, _changingCaptions;
    private StreamInfo _streamInfo = StreamInfo.Empty;
    private Stopwatch? _tuneWatch;
    private Channel? _playingChannel;
    private bool _tuneSubmitted;
    private long _tuneGeneration;
    private readonly Dictionary<string, DateTimeOffset> _failedTunes = new();
    private static string PlayerSettingsPath => Path.Combine(AppStorage.DataDirectory, "player-settings.json");
    private readonly LatestSaveQueue<PlayerSettings> _playerSettingsSaves = new(settings => settings.Save(PlayerSettingsPath));
    private bool _loadingCatalog;
    private readonly PlaylistLibrary _library = AppStorage.CreateLibrary();
    private PlaylistProfile? _activePlaylist;
    private List<ChannelGroup> _allGroups = new();
    private ICollectionView? _groupView;
    private bool _switchingPlaylist;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _guideRefreshGate = new(1, 1);
    private readonly DispatcherTimer _saveSelectionDebounce = new() { Interval = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource _libraryCts = new();
    private bool _catalogReady;
    private bool _libraryLoadedSuccessfully;
    private Task _startupLoad = Task.CompletedTask;
    private readonly PlaybackController _playback = new();
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer _groupSearchDebounce = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
    private readonly DispatcherTimer _rowDebounce = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly HashSet<FrameworkElement> _realizedRows = new();
    private CancellationTokenSource _filterCts = new();
    private bool _minimized;
    private bool _closing;
    private readonly LatestSaveQueue<PlaylistLibrary.LibrarySnapshot> _librarySaves;
    private readonly LatestSaveQueue<Dictionary<string, Dictionary<string, ChannelPreferences.ChannelFlags>>> _preferenceSaves;
    private readonly ChannelPreferences _preferences = new(Path.Combine(AppStorage.DataDirectory, "channel-preferences.json"));
    private readonly ChannelLogoCache _logos = new(Path.Combine(AppStorage.DataDirectory, "logos"));
    private string _providerKey = "";
    private readonly PriorityWorkGate _epgLimiter = new(2);
    private readonly Dictionary<string, Task<IReadOnlyList<ProgramEntry>>> _epgRequests = new();
    private CancellationTokenSource _previewCts = new();
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly DispatcherTimer _epgClock = new() { Interval = TimeSpan.FromMinutes(1) };
    private ListBoxItem? _pressedItem;
    private Channel? _pressedChannel;
    private bool _touchHeld;
    private Channel? _selectedChannel;
    private XtreamClient? _xtream;
    private CancellationTokenSource? _epgCts;
    private CancellationTokenSource? _tuneCts;
    private SourceKind _source = SourceKind.None;

    private enum SourceKind { None, M3u, Xtream }
    private bool _isFullScreen;
    private WindowState _oldWindowState;
    private WindowStyle _oldWindowStyle;
    private ResizeMode _oldResizeMode;
    private bool _oldTopmost;
    private Rect _oldBounds;

    public MainWindow()
    {
        _librarySaves = new(snapshot => { using var timing = App.Measure("library.save"); _library.SaveSnapshot(snapshot); });
        _preferenceSaves = new(_preferences.SaveSnapshot);
        InitializeComponent();
        CaptionsButton.Visibility = BuildInfo.Engine == "mpv" ? Visibility.Visible : Visibility.Collapsed;
        _playback.PlaybackError += () => Dispatcher.BeginInvoke(new Action(() =>
        { if (!_closing) SetStatus("Playback failed. Check the stream and provider permissions."); }));
        _groupSearchDebounce.Tick += (_, _) => { _groupSearchDebounce.Stop(); _groupView?.Refresh();
            if (GroupsList.SelectedItem == null) GroupsList.SelectedItem = _allGroups.FirstOrDefault(FilterGroup); };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RefreshChannels(); };
        _infoTimer.Tick += async (_, _) => await UpdateStreamInfoAsync();
        _volumeDebounce.Tick += async (_, _) => { _volumeDebounce.Stop();
            try { await _playback.VolumeAsync((int)VolumeSlider.Value); } catch (Exception ex) { App.LogException("playback.volume", ex); } };
        _rowDebounce.Tick += (_, _) => { _rowDebounce.Stop(); LoadVisibleRows(); };
        StateChanged += (_, _) =>
        {
            _minimized = WindowState == WindowState.Minimized;
            App.LogDiagnostic(_minimized ? "window.minimized" : "window.restored");
            if (!_minimized) ScheduleVisibleRows();
            // Keep the video HWND and player attached; never Stop, rebind, or refresh the catalog here.
        };
        _channelView = CollectionViewSource.GetDefaultView(_visibleChannels);
        ChannelsList.ItemsSource = _channelView;
        EpgList.ItemsSource = _programs;
        // Defer native VLC initialization until the first actual playback request.
        FavoriteButton.IsEnabled = false;
        _saveSelectionDebounce.Tick += (_, _) => { _saveSelectionDebounce.Stop(); SaveLibrarySafely(); };
        _refreshTimer.Tick += async (_, _) => await RefreshIfDueAsync();
        _refreshTimer.Start();
        // Decrypt and parse large cached catalogs off the UI thread, after the window first appears.
        Loaded += (_, _) => _startupLoad = LoadSavedLibraryAsync();
        _epgClock.Tick += (_, _) =>
        {
            if (_minimized || _closing) return;
            var now = DateTimeOffset.Now;
            foreach (var channel in _realizedRows.Select(r => r.DataContext).OfType<Channel>().Distinct())
                if (_epgRequests.TryGetValue(PlaylistProfile.EpgKey(channel.GroupId, channel.Id), out var request) && request.IsCompletedSuccessfully)
                    channel.SetEpgPreview(request.Result, now);
        };
        _epgClock.Start();
        _holdTimer.Tick += (_, _) =>
        {
            _holdTimer.Stop();
            if (_pressedChannel != null && _pressedItem != null &&
                (_touchHeld || Mouse.LeftButton == MouseButtonState.Pressed) &&
                (_touchHeld || _pressedItem.IsMouseOver))
            {
                ToggleFavorite(_pressedChannel);
                SetStatus((_pressedChannel.IsFavorite ? "Added to" : "Removed from") + " Favorites: " + _pressedChannel.Name);
            }
            _pressedChannel = null;
            _pressedItem = null;
        };
    }


    private async Task LoadSavedLibraryAsync()
    {
        try
        {
            SetStatus("Loading saved playlists...");
            using var timing = App.Measure("library.load");
            await Task.Run(() =>
            {
                _library.Load(); _preferences.Load();
                try { _playerSettings = PlayerSettings.Load(PlayerSettingsPath); }
                catch (Exception ex) { App.LogException("settings.load", ex); }
            });
            RenderCaptionsState(); ApplyColumnLayout();
            BufferSelector.SelectedIndex = _playerSettings.BufferMilliseconds <= 500 ? 0 : _playerSettings.BufferMilliseconds >= 3000 ? 2 : 1;
            if (_closing) return;
            _ = _logos.PruneAsync();
            _libraryLoadedSuccessfully = true;
            _library.Playlists.RemoveAll(p => p.Type is not ("m3u" or "xtream"));
            RefreshPlaylistSelector();
            var restore = _library.Playlists.FirstOrDefault(p => p.Id == _library.SelectedId)
                ?? _library.Playlists.FirstOrDefault();
            if (restore != null)
            {
                await ActivatePlaylistAsync(restore);
                _ = Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(async () => await RefreshActiveAsync(force: false)));
            }
            else SetStatus(AppStorage.HasLegacyVault
                ? "An older encrypted library was preserved. See PORTABILITY.md before migrating; re-add accounts to use this version."
                : "Add an Xtream account or import an M3U playlist to begin.");
        }
        catch (Exception ex)
        {
            App.LogException("Load saved playlists", ex);
            MessageBox.Show(this, "Saved playlists could not be opened. Existing data was left untouched. See PORTABILITY.md for backup recovery.",
                "Playlist library", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SetStatus(string text) => StatusText.Text = text;

    private void RefreshChannels() => _ = FilterAndBindAsync();
    private async Task FilterAndBindAsync()
    {
        if (_closing) return;
        _filterCts.Cancel(); _filterCts.Dispose(); _filterCts = new();
        var token = _filterCts.Token;
        var index = _channelIndex;
        var group = (GroupsList.SelectedItem as ChannelGroup)?.Id ?? "*";
        var query = SearchBox.Text.Trim();
        var hidden = _activePlaylist?.HiddenGroupIds.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
        var selected = ChannelsList.SelectedItem as Channel;
        try
        {
            var filtered = await Task.Run(() =>
            {
                using var timing = App.Measure("catalog.filter");
                return index.Filter(group, query, hidden, token);
            }, token);
            if (token.IsCancellationRequested || _closing) return;
            using var bindTiming = App.Measure("catalog.bind");
            _loadingCatalog = true;
            try
            {
                var scroll = FindChannelScroller(ChannelsList);
                if (_boundGroup.Length > 0 && scroll != null) _groupOffsets[_boundGroup] = scroll.VerticalOffset;
                bool changed = !_visibleChannels.SequenceEqual(filtered);
                if (changed) _visibleChannels.ReplaceAll(filtered);
                if (ChannelsList.ItemsSource == null) ChannelsList.ItemsSource = _channelView;
                ChannelCount.Text = $"{filtered.Count} CHANNELS";
                ChannelsList.SelectedItem = selected != null && filtered.Contains(selected) ? selected : filtered.FirstOrDefault();
                if (changed)
                {
                    double offset = _groupOffsets.GetValueOrDefault(group);
                    _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    { if (!token.IsCancellationRequested && !_closing) FindChannelScroller(ChannelsList)?.ScrollToVerticalOffset(offset); }));
                }
                _boundGroup = group;
            }
            finally { _loadingCatalog = false; }
            if (!ReferenceEquals(_selectedChannel, ChannelsList.SelectedItem) ||
                (_selectedChannel != null && !_epgRequests.ContainsKey(PlaylistProfile.EpgKey(_selectedChannel.GroupId, _selectedChannel.Id))))
                ChannelsList_SelectionChanged(ChannelsList, new SelectionChangedEventArgs(ListBox.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()));
            ScheduleVisibleRows();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogException("catalog.filter", ex); SetStatus("Channel filtering failed. Try the search again."); }
    }

    private async Task<bool> SetChannelsAsync(IEnumerable<Channel> channels, IEnumerable<ChannelGroup> groups,
        SourceKind sourceKind, string sourceName, string providerKey, bool keepPlayback = false)
    {
        var profile = _activePlaylist;
        var token = _libraryCts.Token;
        var preferences = _preferences.CaptureSnapshot().GetValueOrDefault(providerKey);
        _loadingCatalog = true; _catalogReady = false;
        var prepared = await Task.Run(() =>
        {
            using var timing = App.Measure("catalog.prepare");
            var catalog = new List<Channel>();
            foreach (var channel in channels)
            {
                if (token.IsCancellationRequested) break;
                // Presentation state belongs to this catalog; immutable persisted metadata is copied once.
                catalog.Add(new Channel
                {
                    Id = channel.Id, Name = channel.Name, Number = channel.Number,
                    GroupId = channel.GroupId, GroupName = channel.GroupName, Logo = channel.Logo,
                    EpgId = channel.EpgId, Command = channel.Command,
                    IsFavorite = preferences?.GetValueOrDefault(channel.Id)?.Favorite ?? channel.IsFavorite
                });
            }
            var groupList = new List<ChannelGroup> { new("*", "All Channels"), new("fav", "★  Favorites") };
            groupList.AddRange(groups.Where(g => g.Id is not ("*" or "fav")).DistinctBy(g => g.Id).OrderBy(g => g.Name));
            return (catalog, groupList, index: new ChannelIndex(catalog));
        });
        if (token.IsCancellationRequested || !ReferenceEquals(profile, _activePlaylist) || _closing)
        { if (ReferenceEquals(profile, _activePlaylist)) _loadingCatalog = false; return false; }
        _loadingCatalog = true;
        _catalogReady = false;
        try
        {
            CancelAndClear(ref _epgCts);
            CancelAndClear(ref _tuneCts);
            _previewCts.Cancel();
            _previewCts.Dispose();
            _previewCts = new CancellationTokenSource();
            _epgRequests.Clear();
            if (!keepPlayback)
            {
                _ = StopPlaybackAsync();
            }
            _source = sourceKind;
            _providerKey = providerKey;
            var loadedChannels = prepared.catalog;
            var groupList = prepared.groupList;
            _channelIndex = prepared.index;
            // Change the ItemsSource once, instead of notifying WPF thousands of times
            // or raising Reset while selection events are still processing.
            _filterCts.Cancel();
            ChannelsList.ItemsSource = null;
            _channels = loadedChannels;
            _boundGroup = ""; _groupOffsets.Clear();
            _selectedChannel = null;
            _allGroups = groupList;
            GroupsList.ItemsSource = null;
            _groupView = CollectionViewSource.GetDefaultView(_allGroups);
            _groupView.Filter = FilterGroup;
            GroupsList.ItemsSource = _groupView;
            _programs.Clear();
            var preferredGroup = _activePlaylist?.SelectedGroupId ?? "*";
            GroupsList.SelectedItem = _allGroups.FirstOrDefault(g => g.Id == preferredGroup && FilterGroup(g))
                ?? _allGroups.FirstOrDefault();
            ChannelGroupHeading.Text = (GroupsList.SelectedItem as ChannelGroup)?.Name.ToUpperInvariant() ?? "ALL CHANNELS";
        }
        finally { _loadingCatalog = false; }
        RefreshChannels();
        SetStatus($"Loaded {_channels.Count} channels from {sourceName}. Double-click to tune.");
        App.LogDiagnostic($"{sourceKind} catalog bound: {_channels.Count} channels");

        _catalogReady = true;
        ScheduleVisibleRows();
        return true;
    }

    private async void XtreamConnect_Click(object sender, RoutedEventArgs e)
    {
        await _startupLoad;
        if (_closing || !_libraryLoadedSuccessfully) return;
        var dialog = new XtreamDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        XtreamClient? client = null;
        try
        {
            SetImportEnabled(false);
            _libraryCts.Cancel(); _libraryCts.Dispose(); _libraryCts = new();
            SetStatus("Authenticating with Xtream Codes...");
            client = new XtreamClient(dialog.ServerUrl, dialog.Username, dialog.Password, dialog.StreamFormat);
            await client.ConnectAsync(_libraryCts.Token);
            SetStatus("Connected. Fetching Xtream live TV channels...");
            App.LogDiagnostic("Xtream: account authenticated, requesting catalog");
            // Keep large provider JSON mapping/sorting off the WPF dispatcher thread.
            var importToken = _libraryCts.Token;
            var data = await Task.Run(() => client.GetLiveTvAsync(importToken), importToken);
            if (_closing) return;
            App.LogDiagnostic($"Xtream: catalog received ({data.Channels.Count} channels, {data.Groups.Count} groups)");
            var format = client.SelectedFormat;

            _xtream?.Dispose();
            _xtream = client;
            client = null;
            var profile = FindOrCreateProfile("xtream", dialog.ServerUrl, dialog.Username);
            profile.ServerUrl = dialog.ServerUrl; profile.Username = dialog.Username;
            profile.Password = dialog.Password; profile.StreamFormat = dialog.StreamFormat.ToString();
            _activePlaylist = profile;
            if (!await SetChannelsAsync(data.Channels, data.Groups, SourceKind.Xtream, "Xtream Codes (" + format + ")",
                ChannelPreferences.ProviderKey("xtream", dialog.ServerUrl.TrimEnd('/').ToLowerInvariant() + "\n" + dialog.Username))) return;
            CommitCatalog(profile, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            App.LogException("Xtream connect/load", ex);
            SetStatus("Xtream connection failed. Check your server address and account.");
            MessageBox.Show(this, ex.Message, "Xtream Codes", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { client?.Dispose(); SetImportEnabled(true); }
    }

    private async void OpenM3u_Click(object sender, RoutedEventArgs e)
    {
        await _startupLoad;
        if (_closing || !_libraryLoadedSuccessfully) return;
        var picker = new OpenFileDialog
        {
            Title = "Open an M3U / M3U8 playlist",
            Filter = "IPTV playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*"
        };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            SetStatus("Loading playlist...");
            SetImportEnabled(false);
            _libraryCts.Cancel(); _libraryCts.Dispose(); _libraryCts = new();
            var channels = await M3uService.ParseFileAsync(picker.FileName, _libraryCts.Token);
            if (_closing) return;
            if (channels.Count == 0) throw new InvalidDataException("No EXTINF-based channels were found.");

            _xtream?.Dispose();
            _xtream = null;
            var groups = channels.Select(c => c.GroupName).Distinct().Select(name => new ChannelGroup(name, name));
            var profile = FindOrCreateProfile("m3u", Path.GetFullPath(picker.FileName), "");
            profile.LocalPath = await Task.Run(() => AppStorage.ImportM3u(picker.FileName, profile.Id));
            if (_closing) return;
            _activePlaylist = profile;
            if (!await SetChannelsAsync(channels, groups, SourceKind.M3u, Path.GetFileName(picker.FileName),
                ChannelPreferences.ProviderKey("m3u", profile.LocalPath.ToLowerInvariant()))) return;
            CommitCatalog(profile, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            SetStatus("Unable to load playlist: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Open M3U", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetImportEnabled(true); }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingCatalog || _closing) return;
        _searchDebounce.Stop(); _searchDebounce.Start();
    }

    private void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingCatalog || _closing || _channelView == null) return;
        // Abandon guide/logo work from the old category; cached results remain on disk.
        _previewCts.Cancel(); _previewCts.Dispose(); _previewCts = new();
        _epgRequests.Clear();
        ChannelGroupHeading.Text = (GroupsList.SelectedItem as ChannelGroup)?.Name.ToUpperInvariant() ?? "ALL CHANNELS";
        if (_activePlaylist != null)
        {
            _activePlaylist.SelectedGroupId = (GroupsList.SelectedItem as ChannelGroup)?.Id ?? "*";
            _saveSelectionDebounce.Stop();
            _saveSelectionDebounce.Start();
        }
        RefreshChannels();
    }

    private bool FilterGroup(object obj)
    {
        if (obj is not ChannelGroup group) return false;
        if (_activePlaylist?.HiddenGroupIds.Contains(group.Id) == true) return false;
        var search = GroupSearchBox?.Text?.Trim();
        return string.IsNullOrEmpty(search) || group.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void GroupSearchToggle_Click(object sender, RoutedEventArgs e)
    {
        GroupSearchBox.Visibility = GroupSearchBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (GroupSearchBox.Visibility == Visibility.Visible) GroupSearchBox.Focus();
        else GroupSearchBox.Clear();
    }
    private void ChannelSearchToggle_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Visibility = SearchBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (SearchBox.Visibility == Visibility.Visible) SearchBox.Focus();
        else SearchBox.Clear();
    }
    private void GroupSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_groupView == null || _loadingCatalog) return;
        _groupSearchDebounce.Stop(); _groupSearchDebounce.Start();
    }
    private void GroupOptions_Click(object sender, RoutedEventArgs e)
    {
        if (_activePlaylist == null || _loadingCatalog || _closing) return;
        var dialog = new GroupVisibilityDialog(_allGroups, _activePlaylist.HiddenGroupIds) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _activePlaylist.HiddenGroupIds = dialog.Rows.Where(r => !r.Visible).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        _groupView?.Refresh();
        if (GroupsList.SelectedItem == null || !FilterGroup(GroupsList.SelectedItem))
            GroupsList.SelectedItem = _allGroups.FirstOrDefault(FilterGroup);
        SaveLibrarySafely();
        RefreshChannels();
    }

    private void SetImportEnabled(bool enabled)
    {
        // Controls remain usable during networking; prevent conflicting catalog mutations only.
        PlaylistSelector.IsEnabled = enabled;
        TopBar.IsEnabled = enabled;
    }

    private void SavePreferences()
    {
        if (!_libraryLoadedSuccessfully || _preferences.LoadFailed) return;
        _preferenceSaves.Enqueue(_preferences.CaptureSnapshot());
        _ = ObserveSaveAsync(_preferenceSaves.FlushAsync());
    }
    private void ScheduleVisibleRows()
    {
        if (!_catalogReady || _closing || _minimized) return;
        _rowDebounce.Stop(); _rowDebounce.Start();
    }
    private void ChannelRow_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement row) _realizedRows.Add(row);
        ScheduleVisibleRows();
    }
    private void ChannelRow_Unloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement row)
        {
            _realizedRows.Remove(row);
            if (row.FindName("ChannelLogo") is Image image) image.Source = null;
        }
    }
    private void ChannelRow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement row && row.FindName("ChannelLogo") is Image image) image.Source = null;
        ScheduleVisibleRows();
    }
    private void LoadVisibleRows()
    {
        if (!_catalogReady || _closing || _minimized) return;
        if (_tuneWatch != null && _tuneWatch.Elapsed < TimeSpan.FromSeconds(10)) { _rowDebounce.Start(); return; }
        foreach (var row in _realizedRows.ToArray())
            if (row.IsVisible && row.DataContext is Channel channel)
            {
                _ = LoadLogoAsync(row, channel, _previewCts.Token);
                var key = PlaylistProfile.EpgKey(channel.GroupId, channel.Id);
                if (!_epgRequests.ContainsKey(key)) channel.EpgPreviewLoaded = false;
                if (_activePlaylist?.ProviderEpgFetchedAt.TryGetValue(key, out var fetched) == true &&
                    DateTimeOffset.UtcNow - fetched >= TimeSpan.FromMinutes(Math.Clamp(_activePlaylist.EpgRefreshMinutes, 5, 1440)))
                { channel.EpgPreviewLoaded = false; _epgRequests.Remove(key); }
                _ = LoadChannelPreviewAsync(channel, _previewCts.Token);
            }
    }
    private async Task LoadLogoAsync(FrameworkElement row, Channel channel, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(channel.Logo)) return;
            var image = await _logos.GetAsync(channel.Logo,
                TimeSpan.FromHours(Math.Clamp(_activePlaylist?.LogoRefreshHours ?? 24, 1, 168)), token);
            if (token.IsCancellationRequested || !row.IsLoaded || !ReferenceEquals(row.DataContext, channel)) return;
            if (row.FindName("ChannelLogo") is Image target) target.Source = image;
            channel.LogoImage = image == null ? "" : "cached";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { App.LogException("logo.load", ex); }
    }

    private async Task LoadChannelPreviewAsync(Channel channel, CancellationToken token)
    {
        if (!_catalogReady || channel.EpgPreviewLoaded || _source == SourceKind.None)
            return;
        channel.EpgPreviewLoaded = true;
        try
        {
            var programs = await GetProgramsAsync(channel, token);
            if (!token.IsCancellationRequested) channel.SetEpgPreview(programs, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { channel.EpgPreviewLoaded = false; }
        catch (Exception ex)
        {
            channel.EpgPreviewLoaded = false;
            // Individual provider EPG errors should never bring down the channel browser.
            App.LogDiagnostic($"EPG preview failed for channel: {ex.GetType().Name}");
        }
    }

    private bool HasNoCachedGuide(Channel channel)
    {
        return _activePlaylist == null || !_activePlaylist.CachedProviderPrograms.TryGetValue(
            PlaylistProfile.EpgKey(channel.GroupId, channel.Id), out var programs) || programs.Count == 0;
    }

    private Task<IReadOnlyList<ProgramEntry>> GetProgramsAsync(Channel channel, CancellationToken ct)
    {
        var requestKey = PlaylistProfile.EpgKey(channel.GroupId, channel.Id);
        if (_epgRequests.TryGetValue(requestKey, out var existing) && !existing.IsCanceled && !existing.IsFaulted)
        {
            if (ReferenceEquals(_selectedChannel, channel)) _epgLimiter.Promote(requestKey);
            return existing;
        }
        foreach (var key in _epgRequests.Where(kv => kv.Value.IsCompleted).Select(kv => kv.Key).Take(Math.Max(0, _epgRequests.Count - 128)).ToArray())
            _epgRequests.Remove(key);
        var profile = _activePlaylist;
        var source = _source;
        var xtream = _xtream;
        // Cached guide hits must not wait behind two slow provider requests.
        if (profile != null && profile.ProviderEpgFetchedAt.TryGetValue(requestKey, out var cachedAt) &&
            DateTimeOffset.UtcNow - cachedAt < TimeSpan.FromMinutes(Math.Clamp(profile.EpgRefreshMinutes, 5, 1440)))
        {
            var cached = Task.FromResult<IReadOnlyList<ProgramEntry>>(profile.CachedProviderPrograms.GetValueOrDefault(requestKey) ?? []);
            _epgRequests[requestKey] = cached;
            return cached;
        }
        if (_epgRequests.Count >= 128 && !ReferenceEquals(_selectedChannel, channel))
            return Task.FromCanceled<IReadOnlyList<ProgramEntry>>(new CancellationToken(true));
        var task = LoadProgramsAsync();
        _epgRequests[PlaylistProfile.EpgKey(channel.GroupId, channel.Id)] = task;
        return task;

        async Task<IReadOnlyList<ProgramEntry>> LoadProgramsAsync()
        {
            using var lease = await _epgLimiter.EnterAsync(requestKey, ReferenceEquals(_selectedChannel, channel), ct);
            {
                var key = PlaylistProfile.EpgKey(channel.GroupId, channel.Id);
                IReadOnlyList<ProgramEntry> cachedNative = profile != null &&
                    profile.CachedProviderPrograms.TryGetValue(key, out var saved) ? saved : [];
                if ((source == SourceKind.Xtream && xtream == null)
                    && cachedNative.Count > 0) return cachedNative;
                ct.ThrowIfCancellationRequested();
                if (profile != null && profile.ProviderEpgFetchedAt.TryGetValue(key, out var fetched) &&
                    DateTimeOffset.UtcNow - fetched < TimeSpan.FromMinutes(Math.Clamp(profile.EpgRefreshMinutes, 5, 1440)))
                    return cachedNative;
                if (!ReferenceEquals(_selectedChannel, channel) &&
                    !_realizedRows.Any(row => ReferenceEquals(row.DataContext, channel)))
                    throw new OperationCanceledException(ct);
                IReadOnlyList<ProgramEntry> provider;
                try
                {
                    provider = source switch
                    {
                        SourceKind.Xtream when xtream != null => await xtream.GetEpgAsync(channel.Id, ct),
                        _ => []
                    };
                }
                catch (Exception) when (!ct.IsCancellationRequested && cachedNative.Count > 0)
                { return cachedNative; }
                ct.ThrowIfCancellationRequested();
                if (profile != null && _activePlaylist?.Id == profile.Id)
                {
                    profile.ProviderEpgFetchedAt[key] = DateTimeOffset.UtcNow;
                    if (provider.Count > 0) profile.CachedProviderPrograms[key] = provider.Take(24).ToList();
                    if (profile.ProviderEpgFetchedAt.Count > 512)
                    {
                        var oldest = profile.ProviderEpgFetchedAt.MinBy(kv => kv.Value).Key;
                        profile.ProviderEpgFetchedAt.Remove(oldest); profile.CachedProviderPrograms.Remove(oldest);
                    }
                    _saveSelectionDebounce.Stop(); _saveSelectionDebounce.Start();
                }
                return provider.Count > 0 ? provider : cachedNative;
            }
        }
    }

    private void BeginHold(ListBoxItem item)
    {
        _holdTimer.Stop();
        _pressedItem = item;
        _pressedChannel = item.DataContext as Channel;
        if (_pressedChannel != null) _holdTimer.Start();
    }

    private void EndHold()
    {
        _holdTimer.Stop();
        _pressedChannel = null;
        _pressedItem = null;
        _touchHeld = false;
    }

    private void ChannelItem_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_touchHeld && sender is ListBoxItem item && e.ClickCount == 1) BeginHold(item);
        else if (e.ClickCount > 1) EndHold();
    }
    private async void ChannelItem_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var channel = !_touchHeld && e.ClickCount == 1 && sender == _pressedItem ? _pressedChannel : null;
        EndHold();
        if (channel != null) { ChannelsList.SelectedItem = channel; await PlaySelectedAsync(); }
    }
    private void ChannelItem_MouseLeave(object sender, MouseEventArgs e) => EndHold();
    private void ChannelItem_TouchDown(object sender, TouchEventArgs e)
    {
        if (sender is ListBoxItem item) { _touchHeld = true; BeginHold(item); }
    }
    private async void ChannelItem_TouchUp(object sender, TouchEventArgs e)
    {
        var channel = sender == _pressedItem ? _pressedChannel : null;
        EndHold(); e.Handled = true;
        if (channel != null) { ChannelsList.SelectedItem = channel; await PlaySelectedAsync(); }
    }

    // Clear ownership before cancellation: empty categories and repeated Stop events
    // can return without creating a replacement source.
    private static void CancelAndClear(ref CancellationTokenSource? source)
    {
        var previous = source;
        source = null;
        if (previous is null) return;
        try { previous.Cancel(); }
        finally { previous.Dispose(); }
    }

    private async void ChannelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_closing || _loadingCatalog) return;
        CancelAndClear(ref _epgCts);
        _programs.Clear();
        if (ChannelsList.SelectedItem is not Channel channel)
        {
            _selectedChannel = null;
            FavoriteButton.IsEnabled = false;
            SelectedChannelTitle.Text = "Choose a channel";
            SelectedChannelCategory.Text = "";
            EpgState.Text = "No channel selected";
            return;
        }
        _selectedChannel = channel;
        FavoriteButton.IsEnabled = true;
        SelectedChannelTitle.Text = channel.Name;
        SelectedChannelCategory.Text = channel.GroupName + "  •  Channel " + channel.NumberLabel;
        FavoriteButton.Content = channel.IsFavorite ? "★  Favorited" : "☆  Favorite";
        if ((_source == SourceKind.Xtream && _xtream is null && HasNoCachedGuide(channel)) ||
            (_source == SourceKind.M3u && HasNoCachedGuide(channel)))
        {
            EpgState.Text = "No EPG source configured";
            return;
        }
        var cts = new CancellationTokenSource();
        _epgCts = cts;
        EpgState.Text = "Loading...";
        try
        {
            // Allow a click's playback request to get ahead of guide HTTP work.
            await Task.Delay(180, cts.Token);
            while (_tuneWatch != null && _tuneWatch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(200, cts.Token);
            var entries = await GetProgramsAsync(channel, _previewCts.Token).WaitAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            foreach (var item in entries) _programs.Add(item);
            channel.SetEpgPreview(entries, DateTimeOffset.Now);
            EpgState.Text = entries.Count > 0 ? $"{entries.Count} programs" : "No guide data available";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!cts.IsCancellationRequested) EpgState.Text = "Guide unavailable"; }
    }


    private async void Play_Click(object sender, RoutedEventArgs e) => await TuneSelectedAsync(forceRetry: true);

    private Task PlaySelectedAsync() => TuneSelectedAsync(forceRetry: false);
    private async Task TuneSelectedAsync(bool forceRetry)
    {
        if (_closing || _loadingCatalog || ChannelsList.SelectedItem is not Channel channel) return;
        if (!forceRetry && ReferenceEquals(channel, _playingChannel) && _tuneCts != null && !_tuneCts.IsCancellationRequested) return;
        string failureKey = ChannelPreferences.Hash(_providerKey + "|" + channel.GroupId + "|" + channel.Id);
        foreach (var key in _failedTunes.Where(x => x.Value <= DateTimeOffset.UtcNow).Select(x => x.Key).ToArray()) _failedTunes.Remove(key);
        if (!forceRetry && _failedTunes.ContainsKey(failureKey))
        { SetStatus("This channel failed recently. Press Play to retry now."); return; }
        CancelAndClear(ref _tuneCts);
        var cts = new CancellationTokenSource();
        _tuneCts = cts;
        try
        {
            _tuneGeneration++;
            _playingChannel = channel; _tuneWatch = Stopwatch.StartNew(); _tuneSubmitted = false;
            _streamInfo = StreamInfo.Empty; RenderStreamInfo();
            _infoTimer.Interval = TimeSpan.FromMilliseconds(200); _infoTimer.Start();
            SetStatus("Tuning " + channel.Name + "...");
            string url;
            if (_source == SourceKind.Xtream)
            {
                if (_xtream == null) throw new InvalidOperationException("Xtream session is unavailable.");
                url = _xtream.ResolveLiveStream(channel).AbsoluteUri;
            }
            else url = channel.Command;
            if (cts.IsCancellationRequested) return;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("The source did not provide a valid stream URL.");
            using var timing = App.Measure("playback.tune_request");
#if USE_MPV
            await _playback.CaptionsAsync(_playerSettings.CaptionsEnabled, cts.Token);
#endif
            await _playback.PlayAsync(uri, VideoView.VideoHandle, (int)VolumeSlider.Value, cts.Token, _playerSettings.BufferMilliseconds);
            if (cts.IsCancellationRequested || _closing) return;
            _failedTunes.Remove(failureKey);
            _pip?.SetChannelTitle(channel.Name);
            _tuneSubmitted = true;
            SetStatus("Buffering " + channel.Name + "...");
            await UpdateStreamInfoAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                App.LogException("playback.tune", ex);
                if (_failedTunes.Count >= 128) _failedTunes.Remove(_failedTunes.Keys.First());
                _failedTunes[failureKey] = DateTimeOffset.UtcNow.AddSeconds(15);
                SetStatus("Unable to play " + channel.Name + ": " + ex.Message);
                MessageBox.Show(this, ex.Message, "Playback error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        try { await _playback.PauseAsync(); } catch (Exception ex) { App.LogException("playback.pause", ex); }
    }
    private async Task StopPlaybackAsync()
    {
        _tuneGeneration++; _tuneSubmitted = false; _tuneWatch = null; _playingChannel = null;
        _infoTimer.Stop(); _streamInfo = StreamInfo.Empty; RenderStreamInfo();
        try { await _playback.StopAsync(); } catch (Exception ex) { App.LogException("playback.stop", ex); }
    }
    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        CancelAndClear(ref _tuneCts);
        await StopPlaybackAsync();
        if (!_closing) SetStatus("Playback stopped.");
    }

    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedChannel is Channel channel) ToggleFavorite(channel);
    }

    private void ToggleFavorite(Channel channel)
    {
        if (_loadingCatalog || _closing) return;
        channel.IsFavorite = !channel.IsFavorite;
        _preferences.SetFavorite(_providerKey, channel);
        if (ReferenceEquals(_selectedChannel, channel))
            FavoriteButton.Content = channel.IsFavorite ? "★  Favorited" : "☆  Favorite";
        SavePreferences();
        if ((GroupsList.SelectedItem as ChannelGroup)?.Id == "fav") RefreshChannels();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_closing) return;
        _volumeDebounce.Stop(); _volumeDebounce.Start();
    }

    private void ApplyColumnLayout()
    {
        GroupsColumn.MinWidth = 160; ChannelsColumn.MinWidth = 220;
        GroupsColumn.Width = new GridLength(_playerSettings.GroupsColumnWeight, GridUnitType.Star);
        ChannelsColumn.Width = new GridLength(_playerSettings.ChannelsColumnWeight, GridUnitType.Star);
        PlayerColumn.Width = new GridLength(_playerSettings.PlayerColumnWeight, GridUnitType.Star);
    }
    private void ColumnDivider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_isFullScreen || _closing || !_libraryLoadedSuccessfully) return;
        _playerSettings = _playerSettings with { GroupsColumnWeight = GroupsColumn.ActualWidth,
            ChannelsColumnWeight = ChannelsColumn.ActualWidth, PlayerColumnWeight = PlayerColumn.ActualWidth };
        SavePlayerSettings();
    }
    private async void Captions_Click(object sender, RoutedEventArgs e) => await ToggleCaptionsAsync();
    private void RenderCaptionsState()
    {
        CaptionsButton.Content = _playerSettings.CaptionsEnabled ? "CC: On" : "CC: Off";
        System.Windows.Automation.AutomationProperties.SetName(CaptionsButton,
            _playerSettings.CaptionsEnabled ? "Closed captions on" : "Closed captions off");
        _pip?.SetCaptionsEnabled(_playerSettings.CaptionsEnabled);
    }
    private async Task ToggleCaptionsAsync()
    {
#if USE_MPV
        if (_closing || _changingCaptions) return;
        _changingCaptions = true; CaptionsButton.IsEnabled = false;
        try {
            bool enabled = !_playerSettings.CaptionsEnabled;
            await _playback.CaptionsAsync(enabled);
            if (_closing) return;
            _playerSettings = _playerSettings with { CaptionsEnabled = enabled };
            RenderCaptionsState(); SavePlayerSettings();
            SetStatus(enabled ? "Closed captions on — shown when supplied by the channel." : "Closed captions off.");
        } catch (Exception ex) { App.LogException("playback.captions", ex); SetStatus("Unable to change closed captions."); }
        finally { _changingCaptions = false; CaptionsButton.IsEnabled = true; }
#else
        await Task.CompletedTask;
#endif
    }
    private void StreamInfo_Click(object sender, RoutedEventArgs e)
    {
        StreamInfoPanel.Visibility = StreamInfoPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        StreamInfoButton.Content = StreamInfoPanel.Visibility == Visibility.Visible ? "Hide info" : "Info";
        RenderStreamInfo();
        if (StreamInfoPanel.Visibility == Visibility.Visible) { _infoTimer.Start(); _ = UpdateStreamInfoAsync(); }
    }
    private void RenderStreamInfo()
    {
        StreamVideoInfo.Text = "Video: " + _streamInfo.ResolutionText + " · " + _streamInfo.FrameRateText +
            (string.IsNullOrWhiteSpace(_streamInfo.VideoCodec) ? "" : " · " + _streamInfo.VideoCodec);
        StreamBitrateInfo.Text = "Input bitrate: " + _streamInfo.BitrateText + " (received data; varies with buffering)";
        StreamAudioInfo.Text = "Audio: " + _streamInfo.AudioText;
    }
    private async Task UpdateStreamInfoAsync()
    {
        if (_readingInfo || _closing || !_tuneSubmitted || (_minimized && _pip == null)) return;
        if (_tuneWatch == null && _pip == null && StreamInfoPanel.Visibility != Visibility.Visible) { _infoTimer.Stop(); return; }
        _readingInfo = true;
        var generation = _tuneGeneration;
        try {
            var info = await _playback.ReadInfoAsync();
            if (info == null || _closing || generation != _tuneGeneration) return;
            _streamInfo = info;
            _pip?.SetAspect(info.AspectRatio);
            if (StreamInfoPanel.Visibility == Visibility.Visible) RenderStreamInfo();
            if (_tuneWatch != null && info.Playing && info.PositionMilliseconds > 0) {
                App.LogDiagnostic($"playback.time_to_progress_ms={_tuneWatch.Elapsed.TotalMilliseconds:F1}");
                _tuneWatch = null; _infoTimer.Interval = TimeSpan.FromSeconds(1);
                SetStatus("Playing " + _playingChannel?.Name + ".");
            }
        } catch (Exception ex) { App.LogException("playback.info", ex); }
        finally { _readingInfo = false; }
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private static ScrollViewer? FindChannelScroller(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindChannelScroller(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }

    private void BufferSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_libraryLoadedSuccessfully || _closing || BufferSelector.SelectedItem is not ComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), out int buffer)) return;
        _playerSettings = _playerSettings with { BufferMilliseconds = buffer };
        SavePlayerSettings();
        SetStatus("Buffer setting saved. Applies to the next channel you play.");
    }
    private void SavePlayerSettings()
    {
        _playerSettingsSaves.Enqueue(_playerSettings.Normalize());
        _ = ObserveSaveAsync(_playerSettingsSaves.FlushAsync());
    }
    private void Pip_Click(object sender, RoutedEventArgs e) => TogglePictureInPicture();
    private void TogglePictureInPicture()
    {
        if (_closing || !_libraryLoadedSuccessfully) return;
        if (_pip != null) { _pip.Close(); return; }
        if (_isFullScreen) ToggleFullscreen();
        try
        {
            var pip = new PictureInPictureWindow(VideoView, _playerSettings, (int)VolumeSlider.Value,
                async () => { try { await _playback.PauseAsync(); } catch (Exception ex) { App.LogException("pip.pause", ex); } },
                async muted => { try { await _playback.VolumeAsync(muted ? 0 : (int)VolumeSlider.Value); } catch (Exception ex) { App.LogException("pip.mute", ex); } },
                value => VolumeSlider.Value = value,
                settings =>
                {
                    _playerSettings = _playerSettings with { PipLeft = settings.PipLeft, PipTop = settings.PipTop,
                        PipWidth = settings.PipWidth, PipHeight = settings.PipHeight };
                    SavePlayerSettings();
                }, () => _ = ToggleCaptionsAsync(), _playerSettings.CaptionsEnabled);
            _pip = pip;
            pip.SetChannelTitle(_playingChannel?.Name ?? "HN-IPTV");
            pip.SetAspect(_streamInfo.AspectRatio);
            _infoTimer.Start();
            pip.Closed += async (_, _) =>
            {
                _pip = null;
                try
                {
                    if (!_closing)
                    {
                        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                        Activate();
                        await _playback.VolumeAsync((int)VolumeSlider.Value);
                    }
                }
                catch (Exception ex) { App.LogException("pip.volume", ex); }
            };
            pip.Show(); // No Owner: PiP must remain visible when the main window is minimized.
        }
        catch (Exception ex) { _pip = null; App.LogException("pip.open", ex); SetStatus("Picture in picture could not open."); }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullScreen) { ToggleFullscreen(); e.Handled = true; }
        else if (e.Key == Key.P && !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBoxBase && Keyboard.FocusedElement is not PasswordBox)
        { TogglePictureInPicture(); e.Handled = true; }
        else if (e.Key == Key.C && !e.IsRepeat && Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBoxBase && Keyboard.FocusedElement is not PasswordBox)
        { _ = ToggleCaptionsAsync(); e.Handled = BuildInfo.Engine == "mpv"; }
        else if (e.Key == Key.F && Keyboard.FocusedElement is not TextBox)
        { ToggleFullscreen(); e.Handled = true; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo { public int Size; public MonitorRect Monitor; public MonitorRect Work; public int Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private void ToggleFullscreen()
    {
        if (_pip != null) { _pip.Close(); if (_pip != null) return; }
        if (!_isFullScreen)
        {
            _oldWindowStyle = WindowStyle;
            _oldWindowState = WindowState;
            _oldResizeMode = ResizeMode;
            _oldTopmost = Topmost;
            _oldBounds = RestoreBounds;
            var handle = new WindowInteropHelper(this).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
            // WPF positions are device-independent pixels. Monitor bounds are physical pixels.
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                ?? Matrix.Identity;
            var a = transform.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
            var b = transform.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            Left = a.X; Top = a.Y; Width = b.X-a.X; Height = b.Y-a.Y;
            TopBar.Visibility = Visibility.Collapsed;
            StatusStrip.Visibility = Visibility.Collapsed;
            HeaderRow.Height = new GridLength(0);
            FooterRow.Height = new GridLength(0);
            GroupsDivider.Visibility = PlayerDivider.Visibility = Visibility.Collapsed;
            GroupsColumn.MinWidth = ChannelsColumn.MinWidth = 0;
            GroupsColumn.Width = new GridLength(0);
            ChannelsColumn.Width = new GridLength(0);
            PlayerRoot.Margin = new Thickness(0);
            PlayerTitleBar.Visibility = Visibility.Collapsed;
            GuideRoot.Visibility = Visibility.Collapsed;
            PlaybackButtons.Visibility = Visibility.Collapsed;
            StreamInfoPanel.Visibility = Visibility.Collapsed;
            PlayerTitleRow.Height = new GridLength(0);
            PlayerGuideRow.Height = new GridLength(0);
            PlayerControlsRow.Height = new GridLength(0);
            Activate();
        }
        else
        {
            Topmost = _oldTopmost;
            WindowState = WindowState.Normal;
            WindowStyle = _oldWindowStyle;
            ResizeMode = _oldResizeMode;
            Left = _oldBounds.Left; Top = _oldBounds.Top;
            Width = _oldBounds.Width; Height = _oldBounds.Height;
            WindowState = _oldWindowState;
            TopBar.Visibility = Visibility.Visible;
            StatusStrip.Visibility = Visibility.Visible;
            HeaderRow.Height = new GridLength(72);
            FooterRow.Height = new GridLength(35);
            ApplyColumnLayout();
            GroupsDivider.Visibility = PlayerDivider.Visibility = Visibility.Visible;
            PlayerRoot.Margin = new Thickness(20, 19, 20, 12);
            PlayerTitleBar.Visibility = Visibility.Visible;
            GuideRoot.Visibility = Visibility.Visible;
            PlaybackButtons.Visibility = Visibility.Visible;
            if ((string)StreamInfoButton.Content == "Hide info") StreamInfoPanel.Visibility = Visibility.Visible;
            PlayerTitleRow.Height = GridLength.Auto;
            PlayerGuideRow.Height = new GridLength(2, GridUnitType.Star);
            PlayerControlsRow.Height = GridLength.Auto;
        }
        _isFullScreen = !_isFullScreen;
    }

    private PlaylistProfile FindOrCreateProfile(string type, string url, string identity)
    {
        var existing = _library.Playlists.FirstOrDefault(p => p.Type == type &&
            string.Equals(type == "m3u" ? p.LocalPath : p.ServerUrl, url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(type == "xtream" ? p.Username : "", identity, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;
        var profile = new PlaylistProfile
        {
            Type = type,
            Name = type switch
            {
                "m3u" => Path.GetFileNameWithoutExtension(url),
                _ => "Xtream — " + (Uri.TryCreate(url, UriKind.Absolute, out var server) ? server.Host : "Provider")
            }
        };
        _library.Playlists.Add(profile);
        return profile;
    }

    private void CommitCatalog(PlaylistProfile profile, DateTimeOffset now)
    {
        profile.CachedChannels = _channels;
        profile.LastChannelRefresh = now;
        // A channel update also refreshes channel names and logo URLs.
        profile.LastLogoRefresh = now;
        _library.SelectedId = profile.Id;
        RefreshPlaylistSelector();
        SaveLibrarySafely();
    }

    private void RefreshPlaylistSelector()
    {
        _switchingPlaylist = true;
        try
        {
            PlaylistSelector.ItemsSource = null;
            PlaylistSelector.ItemsSource = _library.Playlists.ToList();
            PlaylistSelector.SelectedItem = _library.Playlists.FirstOrDefault(p => p.Id == _activePlaylist?.Id);
        }
        finally { _switchingPlaylist = false; }
    }

    private bool _finishClosing;
    private void SaveLibrarySafely()
    {
        if (!_libraryLoadedSuccessfully) return;
        try { _librarySaves.Enqueue(_library.CaptureSnapshot()); _ = ObserveSaveAsync(_librarySaves.FlushAsync()); }
        catch (Exception ex) { App.LogException("library.snapshot", ex); }
    }

    private async Task ObserveSaveAsync(Task save)
    {
        try { await save; }
        catch (Exception ex)
        {
            App.LogException("storage.save", ex);
            if (!_closing) SetStatus("Changes could not be saved. Check free space and data-folder permissions.");
        }
    }

    private async Task ActivatePlaylistAsync(PlaylistProfile profile)
    {
        _activePlaylist = profile;
        _library.SelectedId = profile.Id;
        var kind = profile.Type switch
        {
            "xtream" => SourceKind.Xtream,
            _ => SourceKind.M3u
        };
        var identity = kind switch
        {
            SourceKind.Xtream => profile.ServerUrl.TrimEnd('/').ToLowerInvariant() + "\n" + profile.Username,
            _ => profile.LocalPath.ToLowerInvariant()
        };
        var providerKey = ChannelPreferences.ProviderKey(profile.Type, identity);

        _xtream?.Dispose(); _xtream = null;
        if (!await SetChannelsAsync(profile.CachedChannels,
            profile.CachedChannels.Select(ch => new ChannelGroup(ch.GroupId, ch.GroupName)),
            kind, profile.Name + " (cached)", providerKey)) return;
        _library.SelectedId = profile.Id;
        RefreshPlaylistSelector();
        SaveLibrarySafely();
    }

    private async void PlaylistSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingPlaylist || PlaylistSelector.SelectedItem is not PlaylistProfile profile ||
            profile.Id == _activePlaylist?.Id) return;
        _libraryCts.Cancel(); _libraryCts.Dispose(); _libraryCts = new();
        await ActivatePlaylistAsync(profile);
        await RefreshActiveAsync(force: false);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshActiveAsync(force: true);
        await RefreshGuideAsync(force: true);
    }

    private async void PlaylistSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_activePlaylist == null) return;
        var dialog = new RefreshSettingsDialog(_activePlaylist) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _activePlaylist.ChannelRefreshHours = dialog.ChannelHours;
        _activePlaylist.EpgRefreshMinutes = dialog.EpgMinutes;
        _activePlaylist.LogoRefreshHours = dialog.LogoHours;
        SaveLibrarySafely();
        await RefreshIfDueAsync();
    }

    private async void RemovePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_activePlaylist == null) return;
        if (MessageBox.Show(this, $"Remove '{_activePlaylist.Name}' from saved playlists?",
            "Remove playlist", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var profile = _activePlaylist;
        _library.Playlists.RemoveAll(p => p.Id == profile.Id);
        _activePlaylist = null;
        _libraryCts.Cancel(); _libraryCts.Dispose(); _libraryCts = new();
        _xtream?.Dispose(); _xtream = null;
        var next = _library.Playlists.FirstOrDefault();
        if (next != null) await ActivatePlaylistAsync(next);
        else
        {
            _library.SelectedId = "";
            await SetChannelsAsync([], [], SourceKind.None, "empty library", "");
            RefreshPlaylistSelector();
        }
        SaveLibrarySafely();
        if (next != null) _ = RefreshActiveAsync(force: false);
    }

    private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (_activePlaylist == null) return;
        var dialog = new RenamePlaylistDialog(_activePlaylist.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _activePlaylist.Name = dialog.PlaylistName;
        RefreshPlaylistSelector(); SaveLibrarySafely();
    }

    private async Task RefreshIfDueAsync()
    {
        if (_activePlaylist == null) return;
        try
        {
            await RefreshActiveAsync(force: false);
            await RefreshGuideAsync(force: false);
        }
        catch (Exception ex) { App.LogException("Scheduled refresh", ex); }
    }

    private async Task RefreshActiveAsync(bool force)
    {
        var profile = _activePlaylist;
        if (profile == null || _closing) return;
        var gateToken = _libraryCts.Token;
        try { await _refreshGate.WaitAsync(gateToken); }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(profile, _activePlaylist) || _closing) { _refreshGate.Release(); return; }
        try
        {
            var now = DateTimeOffset.UtcNow;
            bool needConnection = profile.Type == "xtream" && _xtream == null;
            bool metadataDue = now - profile.LastLogoRefresh >= TimeSpan.FromHours(Math.Clamp(profile.LogoRefreshHours, 1, 168));
            if (!force && !needConnection && !metadataDue &&
                now - profile.LastChannelRefresh < TimeSpan.FromHours(Math.Clamp(profile.ChannelRefreshHours, 1, 168))) return;
            var ct = _libraryCts.Token;
            SetStatus("Refreshing " + profile.Name + " in background...");
            IReadOnlyList<Channel> channels;
            IReadOnlyList<ChannelGroup> groups;
            XtreamClient? newXtream = null;
            try
            {
                if (profile.Type == "m3u")
                {
                    string localPath = AppStorage.ResolveM3u(profile.LocalPath);
                    if (!File.Exists(localPath)) throw new FileNotFoundException("The imported M3U file moved or was deleted.");
                    channels = await M3uService.ParseFileAsync(localPath, ct);
                    groups = await Task.Run(() => ChannelCatalog.GroupsFor(channels), ct);
                }
                else if (profile.Type == "xtream")
                {
                    var format = Enum.TryParse<XtreamStreamFormat>(profile.StreamFormat, true, out var chosen)
                        ? chosen : XtreamStreamFormat.Automatic;
                    newXtream = new XtreamClient(profile.ServerUrl, profile.Username, profile.Password, format);
                    await newXtream.ConnectAsync(ct);
                    if (!force && !metadataDue && profile.CachedChannels.Count > 0 &&
                        now - profile.LastChannelRefresh < TimeSpan.FromHours(Math.Clamp(profile.ChannelRefreshHours, 1, 168)))
                    {
                        if (ct.IsCancellationRequested || _activePlaylist?.Id != profile.Id) return;
                        _xtream?.Dispose(); _xtream = newXtream; newXtream = null;
                        ResetGuidePreviews();
                        SetStatus("Playlist ready.");
                        return;
                    }
                    var connectedClient = newXtream;
                    var result = await Task.Run(() => connectedClient.GetLiveTvAsync(ct), ct);
                    channels = result.Channels; groups = result.Groups;
                }
                else throw new InvalidDataException("Unsupported playlist type. Please re-add it as M3U or Xtream.");
                if (ct.IsCancellationRequested || _activePlaylist?.Id != profile.Id) return;
                if (channels.Count == 0) throw new InvalidDataException("The provider returned no channels; cached data was retained.");
                var oldXtream = _xtream;
                _xtream = newXtream; newXtream = null;
                oldXtream?.Dispose();
                // Avoid replacing playback while channel metadata is refreshed.
                var identity = profile.Type switch
                {
                    "xtream" => profile.ServerUrl.TrimEnd('/').ToLowerInvariant() + "\n" + profile.Username,
                    _ => profile.LocalPath.ToLowerInvariant()
                };
                var kind = profile.Type == "xtream" ? SourceKind.Xtream : SourceKind.M3u;
                if (!await SetChannelsAsync(channels, groups, kind, profile.Name,
                    ChannelPreferences.ProviderKey(profile.Type, identity), keepPlayback: true)) return;
                CommitCatalog(profile, now);
                SetStatus($"Updated {channels.Count} channels — {profile.Name}, {now:HH:mm} UTC");
            }
            finally { newXtream?.Dispose(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.LogException("Playlist background refresh", ex);
            SetStatus("Playlist refresh failed; cached channels are still available.");
        }
        finally { _refreshGate.Release(); }
    }

    private void ResetGuidePreviews()
    {
        _previewCts.Cancel(); _previewCts.Dispose(); _previewCts = new();
        _epgRequests.Clear();
        foreach (var ch in _realizedRows.Select(r => r.DataContext).OfType<Channel>()) ch.EpgPreviewLoaded = false;
        var generation = _previewCts.Token;
        ScheduleVisibleRows();
        if (ChannelsList.SelectedItem is Channel selected)
        {
            _programs.Clear();
            _ = ReloadSelectedGuideAsync(selected, generation);
        }
    }
    private async Task ReloadSelectedGuideAsync(Channel channel, CancellationToken token)
    {
        try
        {
            var guide = await GetProgramsAsync(channel, token);
            if (token.IsCancellationRequested || !ReferenceEquals(ChannelsList.SelectedItem, channel)) return;
            foreach (var entry in guide) _programs.Add(entry);
            EpgState.Text = guide.Count + " programs";
        }
        catch (Exception ex) { App.LogException("Refresh selection guide", ex); }
    }

    // Refresh provider EPG on the configured schedule. Only the selected and visible
    // channels are reloaded; fetching the entire catalog at once could overload the
    // provider and freeze large playlists. Cached provider entries remain as fallback.
    private async Task RefreshGuideAsync(bool force)
    {
        var profile = _activePlaylist;
        if (profile == null || _closing || (_minimized && !force)) return;
        if (!force && DateTimeOffset.UtcNow - profile.LastEpgRefresh <
            TimeSpan.FromMinutes(Math.Clamp(profile.EpgRefreshMinutes, 5, 1440))) return;
        if (!_guideRefreshGate.Wait(0)) return;
        try
        {
            // M3U files do not supply schedules themselves. Keep its existing guide
            // display unchanged; no external matching or downloading takes place.
            if (_source == SourceKind.Xtream)
            {
                if (force) profile.ProviderEpgFetchedAt.Clear();
                ResetGuidePreviews();
                SetStatus("Refreshing provider EPG for visible channels...");
            }
            profile.LastEpgRefresh = DateTimeOffset.UtcNow;
            SaveLibrarySafely();
        }
        finally { _guideRefreshGate.Release(); }
        await Task.CompletedTask;
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_finishClosing) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        if (_pip != null) { _pip.Close(); if (_pip != null) { _closing = false; return; } }
        TopBar.IsEnabled = false;
        _epgClock.Stop(); _refreshTimer.Stop(); _saveSelectionDebounce.Stop(); _infoTimer.Stop(); _volumeDebounce.Stop();
        _searchDebounce.Stop(); _groupSearchDebounce.Stop(); _rowDebounce.Stop(); EndHold();
        _libraryCts.Cancel(); CancelAndClear(ref _epgCts); CancelAndClear(ref _tuneCts);
        _previewCts.Cancel(); _filterCts.Cancel();
        try
        {
            await _startupLoad;
            SaveLibrarySafely(); SavePreferences();
            await Task.WhenAll(_librarySaves.FlushAsync(), _preferenceSaves.FlushAsync(), _playerSettingsSaves.FlushAsync());
        }
        catch (Exception ex)
        {
            App.LogException("shutdown.save", ex);
            MessageBox.Show(this, "Some changes could not be saved. Previous saved data remains intact. Check free space and folder permissions.",
                "HN-IPTV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        try { await _playback.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception ex) { App.LogException("shutdown.playback", ex); }
        _logos.Dispose(); _xtream?.Dispose();
        await App.FlushDiagnosticsAsync();
        _finishClosing = true;
        Close();
        Application.Current.Shutdown();
    }

}
