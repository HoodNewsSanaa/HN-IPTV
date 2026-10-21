using System.IO;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Threading;
using HnIptv.Translation;
namespace HnIptv;

public partial class MainWindow
{
    private TranslationSettings _translationSettings=new();
    private TranslationOverlay? _translationOverlay;
    private readonly TimedCaptions _bufferedCaptions=new();
    private bool _translateSpeech;
    private const bool _translateScreen=false;
    private CancellationTokenSource? _translationSession;
    private readonly CancellationTokenSource _migrationStop=new();
    private readonly TranslationGeneration _translationGeneration=new();
    private readonly SemaphoreSlim _translationGate=new(1,1);
    private Task _speechTranslation=Task.CompletedTask,_cloudTranslation=Task.CompletedTask;
    private ProcessAudioCapture? _audioCapture;
    private BufferedTranslationSource? _bufferedTranslation;
    private bool _bufferPlaybackStopped;
    private Uri? _playingStreamUri;
    private LocalTranslationWorker? _translationWorker;
    private TranslationSettings? _translationWorkerSettings;
    private static string TranslationSettingsPath=>Path.Combine(AppStorage.DataDirectory,"translation-settings.json");
    private void InitializeTranslation()
    {
        Closing+=(_,_)=>_migrationStop.Cancel();
        Loaded+=async(_,_)=>{
            try {
                _translationSettings=await Task.Run(()=>TranslationSettings.Load(TranslationSettingsPath));
                RenderTranslationButtons();
                if(!LocalTranslationRuntime.Ready&&Directory.Exists(Path.Combine(AppStorage.DataDirectory,"translation"))) {
                    SetStatus("Updating speech recognition to Nemotron. Existing downloads are removed after validation.");
                    await LocalTranslationRuntime.InstallAsync(_translationSettings,text=>Dispatcher.BeginInvoke(new Action(()=>SetStatus(text))),_migrationStop.Token);
                }
            }catch(OperationCanceledException){}catch(Exception ex){App.LogException("translation.migration",ex);SetStatus("Nemotron setup needs attention. Open Translation settings to retry; old files have been retained.");}
        };
        Activated+=(_,_)=>{if(_isFullScreen){_fullscreenControls?.Reveal();Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(()=>{if(_isFullScreen&&IsActive){Focus();System.Windows.Input.Keyboard.Focus(this);}}));}};
    }
    private TranslationOverlay CreateTranslationOverlay()=>new(()=>_pip==null?(this,VideoView):(_pip,_pip.VideoHost),()=>_streamInfo.AspectRatio){ControlsWindow=()=>_pip==null&&_isFullScreen?_fullscreenControls:null,SpeechSchedule=_bufferedCaptions,SpeechPlaybackClock=_bufferedTranslation==null?null:()=>_streamInfo.PositionMilliseconds>=0?_streamInfo.PositionMilliseconds/1000d:null};
    private void ResetTranslationOverlay()
    {
        var previous=_translationOverlay;_translationOverlay=null;try{previous?.Close();}catch(InvalidOperationException){}
        if(!_closing&&_translateSpeech&&_playingChannel!=null)_translationOverlay=CreateTranslationOverlay();
    }
    private void RenderTranslationButtons()
    {
        TranslateSpeechButton.Content=_translateSpeech?(_translationSettings.BufferSeconds>0?"Speech: Buffered":"Speech: On"):"Translate speech";
        TranslateScreenButton.Visibility=Visibility.Collapsed;RenderToggleStates();
    }
    private async void TranslationSettings_Click(object sender,RoutedEventArgs e)
    {
        bool wasBuffered=_bufferedTranslation!=null||_bufferPlaybackStopped;
        await StopTranslationSessionAsync();
        var dialog=new TranslationSettingsDialog(_translationSettings){Owner=this};
        if(dialog.ShowDialog()==true){_translationSettings=dialog.Settings;try{_translationSettings.Save(TranslationSettingsPath);}catch(Exception ex){App.LogException("translation.settings.save",ex);SetStatus("Translation settings could not be saved.");}}
        if(wasBuffered&&!_closing&&_playingChannel!=null&&_playingStreamUri!=null) {
            try{await _playback.PlayAsync(_playingStreamUri,VideoView.VideoHandle,_muted?0:(int)VolumeSlider.Value,CancellationToken.None,_playerSettings.BufferMilliseconds,_playingChannel.UserAgent,_playingChannel.Referer);_tuneSubmitted=true;_progressAge.Restart();}
            catch(Exception ex){App.LogException("translation.resume",ex);}
        }
        await RestartTranslationAsync();
    }
    private async void TranslateSpeech_Click(object sender,RoutedEventArgs e)
    {
        bool keyReady=false;try{keyReady=!string.IsNullOrWhiteSpace(AzureTranslationCredentials.Load());}catch(Exception ex){App.LogException("translation.key",ex);}
        if(!_translateSpeech&&(!LocalTranslationRuntime.Ready||!keyReady)){SetStatus("Prepare Nemotron and enter the Azure key in Translation settings.");TranslationSettings_Click(this,new RoutedEventArgs());return;}
        if(!_translateSpeech) {
            var mode=new TranslationModeDialog(_translationSettings.BufferSeconds,BuildInfo.Engine=="mpv"){Owner=this};
            if(mode.ShowDialog()!=true)return;
            _translationSettings=_translationSettings with{BufferSeconds=mode.BufferSeconds};
            try{_translationSettings.Save(TranslationSettingsPath);}catch(Exception ex){App.LogException("translation.settings.save",ex);}
        }
        _translateSpeech=!_translateSpeech;RenderTranslationButtons();await RestartTranslationAsync();
    }
    private void TranslateScreen_Click(object sender,RoutedEventArgs e){}
    private async Task RestartTranslationAsync()
    {
        long generation=_translationGeneration.Advance();CancelAndClear(ref _translationSession);await _translationGate.WaitAsync();
        try {
            await StopTranslationResourcesAsync(!_closing&&_translateSpeech&&_playingChannel!=null&&ReferenceEquals(_translationWorkerSettings,_translationSettings), restoreLive: !_translateSpeech||_translationSettings.BufferSeconds==0);
            if(!_translateSpeech)ResetTranslationOverlay();
            if(!_translationGeneration.IsCurrent(generation)||_closing||!_translateSpeech||_playingChannel==null||!LocalTranslationRuntime.Ready)return;
            _translationOverlay??=CreateTranslationOverlay();_translationOverlay.Clear();
            var session=new CancellationTokenSource();_translationSession=session;var settings=_translationSettings;
            var azure=new AzureTranslatorClient(AzureTranslationCredentials.Load(),settings.AzureRegion);
            bool reused=_translationWorker!=null;
            var worker=_translationWorker??new LocalTranslationWorker(settings);_translationWorker=worker;_translationWorkerSettings=settings;
            if(reused)await worker.ResetAsync(session.Token);
            if(settings.BufferSeconds>0) {
#if USE_MPV
                if(_playingStreamUri==null)throw new InvalidOperationException("No current stream.");
                _tuneSubmitted=false;_streamInfo=HnIptv.Playback.StreamInfo.Empty;CancelRecovery();_bufferPlaybackStopped=true;
                await _playback.StopAsync();
                await BufferedTranslationSource.PrepareAsync(text=>Dispatcher.BeginInvoke(new Action(()=>{SetStatus(text);_translationOverlay?.SetMessage(text,90);})),session.Token);
                await worker.PrepareAsync(session.Token);
                var buffered=new BufferedTranslationSource(_playingStreamUri,_playingChannel.UserAgent,_playingChannel.Referer,settings.BufferSeconds,session.Token);
                _bufferedTranslation=buffered;
                _translationOverlay.SpeechPlaybackClock=()=>_streamInfo.PositionMilliseconds>=0?_streamInfo.PositionMilliseconds/1000d:null;
                var phrases=Channel.CreateBounded<PendingPhrase>(new BoundedChannelOptions(8){FullMode=BoundedChannelFullMode.Wait,SingleReader=true,SingleWriter=true});
                _speechTranslation=RecognizeBufferedAsync(buffered,worker,phrases.Writer,settings,generation,session.Token);
                _cloudTranslation=TranslatePhrasesAsync(phrases.Reader,azure,settings,generation,session.Token,buffered);
                SetStatus("Preparing the "+settings.BufferSeconds+"-second translated buffer...");
                _translationOverlay.SetMessage("Preparing synchronized captions...",90);
                await buffered.WaitReadyAsync(session.Token);
                if(!_translationGeneration.IsCurrent(generation)||session.IsCancellationRequested)return;
                await _playback.TranslationBufferAsync(true);
                await _playback.PlayAsync(buffered.PlaybackUri,VideoView.VideoHandle,_muted?0:(int)VolumeSlider.Value,session.Token,1200);
                _streamInfo=HnIptv.Playback.StreamInfo.Empty;_tuneSubmitted=true;_progressAge.Restart();
                _translationOverlay.SetMessage("Generous time ready — synchronized playback.",3);
#else
                throw new NotSupportedException("Generous time requires the mpv build.");
#endif
            } else {
                _translationOverlay.SpeechPlaybackClock=null;
                _translationOverlay.SetMessage("Preparing Nemotron; buffering app audio...",90);
                var capture=new ProcessAudioCapture();_audioCapture=capture;await capture.StartAsync();
                await worker.PrepareAsync(session.Token);
                if(!_translationGeneration.IsCurrent(generation)||session.IsCancellationRequested)return;
                var phrases=Channel.CreateBounded<PendingPhrase>(new BoundedChannelOptions(4){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=true});
                _speechTranslation=RecognizeAudioAsync(capture,worker,phrases.Writer,settings,generation,session.Token);
                _cloudTranslation=TranslatePhrasesAsync(phrases.Reader,azure,settings,generation,session.Token);
                _translationOverlay.SetMessage("Nemotron → Azure ready ("+worker.Backend+").",3);
            }
        }catch(OperationCanceledException){}catch(Exception ex){App.LogException("translation.start",ex);_translateSpeech=false;RenderTranslationButtons();SetStatus("Translation could not start. Check Nemotron setup and the Azure key/region.");await StopTranslationResourcesAsync(restoreLive:true);}
        finally{_translationGate.Release();}
    }
    private sealed record PendingPhrase(string Text,string Source,long At,double Start=0,double End=0,double Through=0);
    private async Task RecognizeAudioAsync(ProcessAudioCapture capture,LocalTranslationWorker worker,ChannelWriter<PendingPhrase> output,TranslationSettings settings,long generation,CancellationToken ct)
    {
        var phrases=new StreamingPhrases();double previousEnd=0;
        try {
            await foreach(var chunk in capture.Chunks.ReadAllAsync(ct)) {
                if(_userPaused||System.Diagnostics.Stopwatch.GetElapsedTime(chunk.CapturedAt).TotalSeconds>2){previousEnd=0;continue;}
                double duration=chunk.Wave.Length/(double)SpeechAudioBuffer.BytesPerSecond;
                if(Math.Abs(chunk.AudioEndSeconds-duration-previousEnd)>.1){await worker.ResetAsync(ct);phrases.Clear();}
                previousEnd=chunk.AudioEndSeconds;
                var results=await worker.RecognizeAsync(chunk.Wave,ct);
                if(!_translationGeneration.IsCurrent(generation)||ct.IsCancellationRequested)return;
                foreach(var result in results)foreach(string phrase in phrases.Update(result.Text,result.Final,chunk.AudioEndSeconds))
                    output.TryWrite(new(phrase,settings.InputLanguage=="auto"?TranslationSettings.NormalizeLanguage(result.Language):settings.InputLanguage,chunk.CapturedAt));
            }
        }catch(OperationCanceledException){}catch(Exception ex){if(!ct.IsCancellationRequested){App.LogException("translation.recognition",ex);TranslationFailed("Nemotron recognition stopped. Check Translation settings.");}}
        finally{output.TryComplete();}
    }
    private async Task RecognizeBufferedAsync(BufferedTranslationSource buffered,LocalTranslationWorker worker,ChannelWriter<PendingPhrase> output,TranslationSettings settings,long generation,CancellationToken ct)
    {
        double batchStart=0,through=0,start=double.PositiveInfinity,end=0;string language="auto";var text=new List<string>();
        void Collect(IReadOnlyList<RecognitionText> results) {
            foreach(var result in results.Where(r=>r.Final&&r.Text.Any(char.IsLetterOrDigit))) {
                text.Add(result.Text);language=result.Language;
                start=Math.Min(start,batchStart+result.StartSeconds);end=Math.Max(end,batchStart+result.EndSeconds);
            }
        }
        async Task Flush() {
            Collect(await worker.FinishAsync(ct));
            if(!_translationGeneration.IsCurrent(generation)||ct.IsCancellationRequested)return;
            string paragraph=string.Join(" ",text);
            if(paragraph.Length>0&&(!double.IsFinite(start)||end<=start))throw new IOException("Nemotron did not return usable speech timestamps.");
            await output.WriteAsync(new(paragraph,settings.InputLanguage=="auto"?TranslationSettings.NormalizeLanguage(language):settings.InputLanguage,System.Diagnostics.Stopwatch.GetTimestamp(),start,end,through),ct);
            await worker.ResetAsync(ct);batchStart=through;start=double.PositiveInfinity;end=0;text.Clear();
        }
        try {
            await foreach(var chunk in buffered.Chunks.ReadAllAsync(ct)) {
                through=chunk.AudioEndSeconds;var recognized=await worker.RecognizeAsync(chunk.Wave,ct);Collect(recognized);
                if(!_translationGeneration.IsCurrent(generation)||ct.IsCancellationRequested)return;
                // Prefer a completed utterance after six seconds; cap a continuous speaker to retain headroom.
                double maximum=settings.BufferSeconds==10?8:12;
                if((through-batchStart>=6&&recognized.Any(r=>r.Final&&r.Text.Any(char.IsLetterOrDigit)))||through-batchStart>=maximum)await Flush();
            }
            if(through>batchStart)await Flush();
        }catch(OperationCanceledException){}catch(Exception ex){if(!ct.IsCancellationRequested){App.LogException("translation.buffer.recognition",ex);TranslationFailed("The translated buffer stopped. Retry Translate or choose Live.");}}
        finally{output.TryComplete();}
    }
    private async Task TranslatePhrasesAsync(ChannelReader<PendingPhrase> input,AzureTranslatorClient azure,TranslationSettings settings,long generation,CancellationToken ct,BufferedTranslationSource? buffered=null)
    {
        try {
            await foreach(var phrase in input.ReadAllAsync(ct)) {
                if(buffered==null&&System.Diagnostics.Stopwatch.GetElapsedTime(phrase.At).TotalSeconds>8)continue;
                try {
                    string text=phrase.Text.Length==0?"":await azure.TranslateAsync(phrase.Text,phrase.Source,settings.Target,ct);
                    if(ct.IsCancellationRequested||!_translationGeneration.IsCurrent(generation))return;
                    if(buffered!=null) {
                        _translationOverlay?.ScheduleSpeech(text,phrase.Start,phrase.End);
                        buffered.TranslatedThrough(phrase.Through);
                    }else if(System.Diagnostics.Stopwatch.GetElapsedTime(phrase.At).TotalSeconds<=10)_translationOverlay?.QueueSpeech(StreamingPhrases.Cues(text));
                }catch(OperationCanceledException){throw;}
                catch(Exception ex){App.LogException("translation.azure",ex);SetStatus(ex is InvalidOperationException?ex.Message:"Azure translation failed. Check the connection, key and region.");if(buffered!=null){TranslationFailed("Azure could not prepare synchronized captions. Buffered translation stopped; retry Translate.");return;}}
            }
        }catch(OperationCanceledException){}
    }
    private void TranslationFailed(string message){SetStatus(message);_translationOverlay?.SetMessage(message,10);_translateSpeech=false;RenderTranslationButtons();Dispatcher.BeginInvoke(new Action(()=>{if(!_closing)_=RestartTranslationAsync();}));}
    private async Task StopTranslationSessionAsync(bool preserveWorker=false)
    {
        _translationGeneration.Advance();CancelAndClear(ref _translationSession);var overlay=_translationOverlay;_translationOverlay=null;overlay?.Close();
        await _translationGate.WaitAsync();try{await StopTranslationResourcesAsync(preserveWorker);}finally{_translationGate.Release();}
    }
    private async Task StopTranslationResourcesAsync(bool preserveWorker=false,bool restoreLive=false)
    {
        bool hadBuffer=_bufferedTranslation!=null||_bufferPlaybackStopped;_bufferPlaybackStopped=false;
        if(_bufferedTranslation!=null) {
#if USE_MPV
            await _playback.StopAsync();await _playback.TranslationBufferAsync(false);
#endif
            await _bufferedTranslation.DisposeAsync();_bufferedTranslation=null;
        }
        if(_translationOverlay!=null)_translationOverlay.SpeechPlaybackClock=null;
        _bufferedCaptions.Clear();
        bool retained=preserveWorker&&_translationWorker?.CanReuse==true;
        if(!retained&&_translationWorker!=null){await _translationWorker.DisposeAsync();_translationWorker=null;_translationWorkerSettings=null;}
        if(_audioCapture!=null){await _audioCapture.DisposeAsync();_audioCapture=null;}
        try{await Task.WhenAll(_speechTranslation,_cloudTranslation).WaitAsync(TimeSpan.FromSeconds(5));}catch{}
        if(retained&&_translationWorker?.CanReuse!=true&&_translationWorker!=null){await _translationWorker.DisposeAsync();_translationWorker=null;_translationWorkerSettings=null;}
        _speechTranslation=_cloudTranslation=Task.CompletedTask;
        if(hadBuffer&&restoreLive&&!_closing&&_playingChannel!=null&&_playingStreamUri!=null) {
            await _playback.PlayAsync(_playingStreamUri,VideoView.VideoHandle,_muted?0:(int)VolumeSlider.Value,CancellationToken.None,_playerSettings.BufferMilliseconds,_playingChannel.UserAgent,_playingChannel.Referer);
            _tuneSubmitted=true;_progressAge.Restart();
        }
    }
}
