using System.IO;
using System.Windows;
using System.Windows.Threading;
using HnIptv.Translation;
namespace HnIptv;

public partial class MainWindow
{
    private TranslationSettings _translationSettings = new();
    private TranslationOverlay? _translationOverlay;
    private bool _translateSpeech, _translateScreen;
    private string _translationBackend = "";
    private CancellationTokenSource? _translationSession;
    private readonly TranslationGeneration _translationGeneration = new();
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private Task _speechTranslation = Task.CompletedTask, _screenTranslation = Task.CompletedTask;
    private ProcessAudioCapture? _audioCapture;
    private LocalTranslationWorker? _translationWorker;
    private bool _audioInference;
    private long _emptyScreenMessageAt;
    private static string TranslationSettingsPath => Path.Combine(AppStorage.DataDirectory, "translation-settings.json");

    private void InitializeTranslation()
    {
        Loaded += async (_, _) => {
            try { _translationSettings = await Task.Run(() => TranslationSettings.Load(TranslationSettingsPath)); }
            catch (Exception ex) { App.LogException("translation.settings", ex); }
            RenderTranslationButtons();
        };
        Activated += (_, _) => {
            if (_isFullScreen) {
                _fullscreenControls?.Reveal();
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => {
                    if (_isFullScreen && IsActive) { Focus(); System.Windows.Input.Keyboard.Focus(this); }
                }));
            }
        };
    }
    private TranslationOverlay CreateTranslationOverlay() => new(() =>
        _pip == null ? (this, VideoView) : (_pip, _pip.VideoHost), () => _streamInfo.AspectRatio) {
            ControlsWindow = () => _pip == null && _isFullScreen ? _fullscreenControls : null
        };
    private void ResetTranslationOverlay()
    {
        var previous = _translationOverlay; _translationOverlay = null;
        try { previous?.Close(); } catch (InvalidOperationException) { }
        if (!_closing && (_translateSpeech || _translateScreen) && _playingChannel != null)
            _translationOverlay = CreateTranslationOverlay();
    }
    private void RenderTranslationButtons()
    {
        TranslateSpeechButton.Content = _translateSpeech ? "Speech: On" : "Translate speech";
        TranslateScreenButton.Content = _translateScreen ? "Text: On" : "Translate text";
        TranslateScreenButton.IsEnabled = BuildInfo.Engine == "mpv";
        RenderToggleStates();
    }
    private async void TranslationSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TranslationSettingsDialog(_translationSettings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        _translationSettings = dialog.Settings;
        try { await Task.Run(() => _translationSettings.Save(TranslationSettingsPath)); }
        catch (Exception ex) { App.LogException("translation.settings.save", ex); SetStatus("Translation settings could not be saved."); }
        await RestartTranslationAsync();
    }
    private bool TranslationReady()
    {
        if (LocalTranslationRuntime.Ready) return true;
        SetStatus("Prepare local models in Translation settings first. Models download once and stay cached.");
        TranslationSettings_Click(this, new RoutedEventArgs());
        return LocalTranslationRuntime.Ready;
    }
    private async void TranslateSpeech_Click(object sender, RoutedEventArgs e)
    {
        if (!_translateSpeech && !TranslationReady()) return;
        _translateSpeech = !_translateSpeech; RenderTranslationButtons();
        await RestartTranslationAsync();
        SetStatus(_translateSpeech ? (_translationSettings.Provider == "azure" ? "Azure speech translation on. Only recognized text is sent online." : "Local speech translation on. Captions follow speech with a processing delay.") : "Speech translation off.");
    }
    private async void TranslateScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_translateScreen && !TranslationReady()) return;
        _translateScreen = !_translateScreen; RenderTranslationButtons();
        await RestartTranslationAsync();
        SetStatus(_translateScreen ? (_translationSettings.Provider == "azure" ? "Azure screen-text translation on. Only recognized text is sent online." : "Local screen-text translation on. Auto uses installed language models.") : "Screen-text translation off.");
    }
    private async Task RestartTranslationAsync()
    {
        _translationBackend = "";
        long generation = _translationGeneration.Advance();
        CancelAndClear(ref _translationSession);
        await _translationGate.WaitAsync();
        try
        {
            await StopTranslationResourcesAsync();
            if (!_translationGeneration.IsCurrent(generation) || _closing ||
                (!_translateSpeech && !_translateScreen) || _playingChannel == null || !LocalTranslationRuntime.Ready) return;
            if (_translationOverlay == null || _translationOverlay.IsClosed) _translationOverlay = CreateTranslationOverlay();
            _translationOverlay.Clear();
            var session = new CancellationTokenSource();
            _translationSession = session;
            var settings = _translationSettings;
            var worker = new LocalTranslationWorker(settings);
            _translationWorker = worker;
            _translationOverlay.SetMessage("Preparing local translation models…", 90);
            // One process shares a CUDA context. Warm up before loopback starts so startup cannot drop speech.
            foreach (var mode in new[] { "screen", "audio" })
            {
                if (mode == "audio" ? !_translateSpeech : !_translateScreen) continue;
                var prepared = await worker.TranslateAsync("prepare_" + mode, [], settings, session.Token);
                if (session.IsCancellationRequested || !_translationGeneration.IsCurrent(generation)) return;
                if (prepared.Error != null) {
                    if (mode == "audio") _translateSpeech = false; else _translateScreen = false;
                    ApplyTranslation(prepared, mode == "audio");
                    RenderTranslationButtons();
                }
            }
            if (!_translateSpeech && !_translateScreen) { await StopTranslationResourcesAsync(); return; }
            _translationOverlay.SetMessage(settings.Provider == "azure" ? "Azure text translation ready." : "Local translation ready.", 3);
            if (_translateSpeech)
            {
                var capture = new ProcessAudioCapture();
                _audioCapture = capture;
                try {
                    await capture.StartAsync();
                    if (session.IsCancellationRequested) return;
                    _speechTranslation = TranslateAudioLoopAsync(capture, worker, settings, generation, session.Token);
                }
                catch (OperationCanceledException) when (session.IsCancellationRequested) { return; }
                catch (Exception ex) {
                    App.LogException("translation.audio.start", ex);
                    _translationOverlay.SetMessage("Could not capture this app's audio.", 8);
                    _translateSpeech = false; RenderTranslationButtons();
                    await capture.DisposeAsync(); _audioCapture = null;
                }
            }
#if USE_MPV
            if (_translateScreen && !session.IsCancellationRequested)
                _screenTranslation = TranslateVideoLoopAsync(worker, settings, generation, session.Token);
#endif
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            App.LogException("translation.start", ex); SetStatus(_translationSettings.Provider == "azure" ? "Azure translation could not start. Check the key and region in the Azure settings tab." : "Local translation could not start. Check Translation settings.");
            _translationOverlay?.SetMessage(_translationSettings.Provider == "azure" ? "Check the key and region in Azure settings." : "Local translation could not start. Check Translation settings.", 10);
            _translateSpeech = _translateScreen = false; RenderTranslationButtons();
            await StopTranslationResourcesAsync();
        }
        finally { _translationGate.Release(); }
    }
    private async Task TranslateAudioLoopAsync(ProcessAudioCapture capture, LocalTranslationWorker worker,
        TranslationSettings settings, long generation, CancellationToken ct)
    {
        try {
            await foreach (var chunk in capture.Chunks.ReadAllAsync(ct))
            {
                if (_userPaused || System.Diagnostics.Stopwatch.GetElapsedTime(chunk.CapturedAt).TotalSeconds > 4) continue;
                _audioInference = true;
                TranslationResult result;
                try { result = await worker.TranslateAsync("audio", chunk.Wave, settings, ct, chunk.ContextSeconds, chunk.AudioEndSeconds); }
                finally { _audioInference = false; }
                if (ct.IsCancellationRequested || !_translationGeneration.IsCurrent(generation)) return;
                ApplyTranslation(result, true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { App.LogException("translation.audio", ex); TranslationFailed(true, "Speech translation stopped. Check the local models in Translation settings."); } }
    }
#if USE_MPV
    private async Task TranslateVideoLoopAsync(LocalTranslationWorker worker, TranslationSettings settings,
        long generation, CancellationToken ct)
    {
        byte[]? pausedFrame = null;
        try {
            while (!ct.IsCancellationRequested)
            {
                var cycle = System.Diagnostics.Stopwatch.StartNew();
                if ((!_minimized || _pip != null) && !_audioInference)
                {
                    byte[]? frame = null;
                    try {
                        frame = _userPaused && pausedFrame != null ? pausedFrame : await _playback.CaptureVideoFrameAsync(ct);
                        pausedFrame = frame;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) {
                        App.LogException("translation.screen.capture", ex);
                        _translationOverlay?.SetMessage("Video frame unavailable; retrying…");
                    }
                    if (frame != null)
                    {
                        var result = await worker.TranslateAsync("screen", frame, settings, ct);
                        if (ct.IsCancellationRequested || !_translationGeneration.IsCurrent(generation)) return;
                        ApplyTranslation(result, false);
                    }
                }
                await Task.Delay(Math.Max(200, 500 - (int)cycle.ElapsedMilliseconds), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { App.LogException("translation.screen", ex); TranslationFailed(false, "Screen translation stopped. Check Translation settings."); } }
    }
#endif
    private void ApplyTranslation(TranslationResult result, bool speech)
    {
        if (_closing) return;
        if (result.Error != null) {
            SetStatus(result.Error);
            _translationOverlay?.SetMessage(result.Error, 10);
            return;
        }
        string backend = result.Backend + result.Notice;
        if (!string.IsNullOrEmpty(result.Backend) && backend != _translationBackend) {
            _translationBackend = backend;
            SetStatus("Local translation: " + result.Backend + (result.Notice == null ? "." : ". " + result.Notice));
        }
        if (speech) {
            if (result.SpeechCues != null) _translationOverlay?.QueueSpeech(result.SpeechCues);
            else _translationOverlay?.SetSpeech(result.Text, result.ReplaceSpeech);
        }
        else {
            if (_translationSettings.ScreenOverlay == "positioned" && result.Regions != null)
                _translationOverlay?.SetScreenRegions(result.Regions, result.FrameWidth, result.FrameHeight, result.Target);
            else _translationOverlay?.SetScreen(result.Text);
            if (string.IsNullOrWhiteSpace(result.Text) && (_emptyScreenMessageAt == 0 ||
                System.Diagnostics.Stopwatch.GetElapsedTime(_emptyScreenMessageAt).TotalSeconds > 15)) {
                _emptyScreenMessageAt = System.Diagnostics.Stopwatch.GetTimestamp();
                _translationOverlay?.SetMessage("Scanning video: no readable foreign-language text found.", 3);
            }
        }
    }
    private void TranslationFailed(bool speech, string message)
    {
        SetStatus(message); _translationOverlay?.SetMessage(message, 10);
        if (speech) _translateSpeech = false; else _translateScreen = false;
        RenderTranslationButtons();
        Dispatcher.BeginInvoke(new Action(() => { if (!_closing) _ = RestartTranslationAsync(); }));
    }
    private async Task StopTranslationSessionAsync()
    {
        _translationGeneration.Advance();
        CancelAndClear(ref _translationSession);
        var overlay = _translationOverlay; _translationOverlay = null; overlay?.Close();
        await _translationGate.WaitAsync();
        try { await StopTranslationResourcesAsync(); }
        finally { _translationGate.Release(); }
    }
    private async Task StopTranslationResourcesAsync()
    {
        if (_translationWorker != null) { await _translationWorker.DisposeAsync(); _translationWorker = null; }
        if (_audioCapture != null) { await _audioCapture.DisposeAsync(); _audioCapture = null; }
        try { await Task.WhenAll(_speechTranslation, _screenTranslation).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        _speechTranslation = _screenTranslation = Task.CompletedTask;
    }
}
