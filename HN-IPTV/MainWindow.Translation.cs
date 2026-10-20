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
    private CancellationTokenSource? _translationSession;
    private readonly TranslationGeneration _translationGeneration = new();
    private readonly SemaphoreSlim _translationGate = new(1, 1);
    private Task _speechTranslation = Task.CompletedTask, _screenTranslation = Task.CompletedTask;
    private ProcessAudioCapture? _audioCapture;
    private LocalTranslationWorker? _speechWorker, _screenWorker;
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
    private void RenderTranslationButtons()
    {
        TranslateSpeechButton.Content = _translateSpeech ? "Speech: On" : "Translate speech";
        TranslateScreenButton.Content = _translateScreen ? "Text: On" : "Translate text";
        TranslateScreenButton.IsEnabled = BuildInfo.Engine == "mpv";
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
        SetStatus(_translateSpeech ? "Local speech translation on. Captions follow speech with a processing delay." : "Speech translation off.");
    }
    private async void TranslateScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_translateScreen && !TranslationReady()) return;
        _translateScreen = !_translateScreen; RenderTranslationButtons();
        await RestartTranslationAsync();
        SetStatus(_translateScreen ? "Local screen-text translation on. Auto uses installed language models." : "Screen-text translation off.");
    }
    private async Task RestartTranslationAsync()
    {
        long generation = _translationGeneration.Advance();
        CancelAndClear(ref _translationSession);
        await _translationGate.WaitAsync();
        try
        {
            await StopTranslationResourcesAsync();
            if (!_translationGeneration.IsCurrent(generation) || _closing ||
                (!_translateSpeech && !_translateScreen) || _playingChannel == null || !LocalTranslationRuntime.Ready) return;
            _translationOverlay ??= new TranslationOverlay(() =>
                _pip == null ? (this, VideoView) : (_pip, _pip.VideoHost));
            _translationOverlay.Clear();
            var session = new CancellationTokenSource();
            _translationSession = session;
            var settings = _translationSettings;
            if (_translateSpeech)
            {
                var capture = new ProcessAudioCapture();
                _audioCapture = capture;
                try {
                    await capture.StartAsync();
                    if (session.IsCancellationRequested) return;
                    var worker = new LocalTranslationWorker(settings); _speechWorker = worker;
                    _speechTranslation = TranslateAudioLoopAsync(capture, worker, settings, generation, session.Token);
                }
                catch (Exception ex) {
                    App.LogException("translation.audio.start", ex);
                    SetStatus("Could not capture this app's audio. Speech translation needs Windows 11 or build 20348 and newer.");
                    _translateSpeech = false; RenderTranslationButtons();
                    await capture.DisposeAsync(); _audioCapture = null;
                }
            }
#if USE_MPV
            if (_translateScreen && !session.IsCancellationRequested)
            {
                var worker = new LocalTranslationWorker(settings); _screenWorker = worker;
                _screenTranslation = TranslateVideoLoopAsync(worker, settings, generation, session.Token);
            }
#endif
        }
        catch (Exception ex) { App.LogException("translation.start", ex); SetStatus("Local translation could not start. Check Translation settings."); }
        finally { _translationGate.Release(); }
    }
    private async Task TranslateAudioLoopAsync(ProcessAudioCapture capture, LocalTranslationWorker worker,
        TranslationSettings settings, long generation, CancellationToken ct)
    {
        try {
            await foreach (var chunk in capture.Chunks.ReadAllAsync(ct))
            {
                if (_userPaused) continue;
                var result = await worker.TranslateAsync("audio", chunk, settings, ct);
                if (ct.IsCancellationRequested || !_translationGeneration.IsCurrent(generation)) return;
                ApplyTranslation(result, true);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { App.LogException("translation.audio", ex); SetStatus("Speech translation stopped. Check the local models in Translation settings."); } }
    }
#if USE_MPV
    private async Task TranslateVideoLoopAsync(LocalTranslationWorker worker, TranslationSettings settings,
        long generation, CancellationToken ct)
    {
        string? lastFrame = null;
        string lastScreenText = "";
        try {
            while (!ct.IsCancellationRequested)
            {
                if (!_userPaused && (!_minimized || _pip != null))
                {
                    var frame = await _playback.CaptureVideoFrameAsync(ct);
                    if (frame != null)
                    {
                        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frame));
                        if (digest != lastFrame)
                        {
                            lastFrame = digest;
                            var result = await worker.TranslateAsync("screen", frame, settings, ct);
                            if (ct.IsCancellationRequested || !_translationGeneration.IsCurrent(generation)) return;
                            lastScreenText = result.Error == null ? result.Text : "";
                            ApplyTranslation(result, false);
                        }
                        else _translationOverlay?.SetScreen(lastScreenText);
                    }
                }
                else if (_userPaused) _translationOverlay?.SetScreen(lastScreenText);
                await Task.Delay(2000, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) { App.LogException("translation.screen", ex); SetStatus("Screen translation stopped. Check the installed language models."); } }
    }
#endif
    private void ApplyTranslation(TranslationResult result, bool speech)
    {
        if (_closing) return;
        if (result.Error != null) {
            SetStatus(result.Error);
            if (speech) _translationOverlay?.SetSpeech(""); else _translationOverlay?.SetScreen("");
            return;
        }
        if (speech) _translationOverlay?.SetSpeech(result.Text); else _translationOverlay?.SetScreen(result.Text);
    }
    private async Task StopTranslationSessionAsync()
    {
        _translationGeneration.Advance();
        CancelAndClear(ref _translationSession);
        _translationOverlay?.Close(); _translationOverlay = null;
        await _translationGate.WaitAsync();
        try { await StopTranslationResourcesAsync(); }
        finally { _translationGate.Release(); }
    }
    private async Task StopTranslationResourcesAsync()
    {
        if (_speechWorker != null) { await _speechWorker.DisposeAsync(); _speechWorker = null; }
        if (_screenWorker != null) { await _screenWorker.DisposeAsync(); _screenWorker = null; }
        if (_audioCapture != null) { await _audioCapture.DisposeAsync(); _audioCapture = null; }
        try { await Task.WhenAll(_speechTranslation, _screenTranslation).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        _speechTranslation = _screenTranslation = Task.CompletedTask;
    }
}
