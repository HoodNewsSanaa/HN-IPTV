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
    private bool _translateSpeech;
    private const bool _translateScreen=false;
    private CancellationTokenSource? _translationSession;
    private readonly CancellationTokenSource _migrationStop=new();
    private readonly TranslationGeneration _translationGeneration=new();
    private readonly SemaphoreSlim _translationGate=new(1,1);
    private Task _speechTranslation=Task.CompletedTask,_cloudTranslation=Task.CompletedTask;
    private ProcessAudioCapture? _audioCapture;
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
    private TranslationOverlay CreateTranslationOverlay()=>new(()=>_pip==null?(this,VideoView):(_pip,_pip.VideoHost),()=>_streamInfo.AspectRatio){ControlsWindow=()=>_pip==null&&_isFullScreen?_fullscreenControls:null};
    private void ResetTranslationOverlay()
    {
        var previous=_translationOverlay;_translationOverlay=null;try{previous?.Close();}catch(InvalidOperationException){}
        if(!_closing&&_translateSpeech&&_playingChannel!=null)_translationOverlay=CreateTranslationOverlay();
    }
    private void RenderTranslationButtons()
    {
        TranslateSpeechButton.Content=_translateSpeech?"Speech: On":"Translate speech";
        TranslateScreenButton.Visibility=Visibility.Collapsed;RenderToggleStates();
    }
    private async void TranslationSettings_Click(object sender,RoutedEventArgs e)
    {
        await StopTranslationSessionAsync();
        var dialog=new TranslationSettingsDialog(_translationSettings){Owner=this};
        if(dialog.ShowDialog()==true){_translationSettings=dialog.Settings;try{_translationSettings.Save(TranslationSettingsPath);}catch(Exception ex){App.LogException("translation.settings.save",ex);SetStatus("Translation settings could not be saved.");}}
        await RestartTranslationAsync();
    }
    private async void TranslateSpeech_Click(object sender,RoutedEventArgs e)
    {
        if(!_translateSpeech&&(!LocalTranslationRuntime.Ready||string.IsNullOrWhiteSpace(AzureTranslationCredentials.Load()))){SetStatus("Prepare Nemotron and enter the Azure key in Translation settings.");TranslationSettings_Click(this,new RoutedEventArgs());return;}
        _translateSpeech=!_translateSpeech;RenderTranslationButtons();await RestartTranslationAsync();
    }
    private void TranslateScreen_Click(object sender,RoutedEventArgs e){}
    private async Task RestartTranslationAsync()
    {
        long generation=_translationGeneration.Advance();CancelAndClear(ref _translationSession);await _translationGate.WaitAsync();
        try {
            await StopTranslationResourcesAsync(!_closing&&_translateSpeech&&_playingChannel!=null&&ReferenceEquals(_translationWorkerSettings,_translationSettings));
            if(!_translationGeneration.IsCurrent(generation)||_closing||!_translateSpeech||_playingChannel==null||!LocalTranslationRuntime.Ready)return;
            _translationOverlay??=CreateTranslationOverlay();_translationOverlay.Clear();
            var session=new CancellationTokenSource();_translationSession=session;var settings=_translationSettings;
            var azure=new AzureTranslatorClient(AzureTranslationCredentials.Load(),settings.AzureRegion);
            bool reused=_translationWorker!=null;
            var worker=_translationWorker??new LocalTranslationWorker(settings);_translationWorker=worker;_translationWorkerSettings=settings;
            if(reused)await worker.ResetAsync(session.Token);
            _translationOverlay.SetMessage("Preparing Nemotron; buffering app audio...",90);
            var capture=new ProcessAudioCapture();_audioCapture=capture;await capture.StartAsync();
            await worker.PrepareAsync(session.Token);
            if(!_translationGeneration.IsCurrent(generation)||session.IsCancellationRequested)return;
            var phrases=Channel.CreateBounded<PendingPhrase>(new BoundedChannelOptions(4){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=true});
            _speechTranslation=RecognizeAudioAsync(capture,worker,phrases.Writer,settings,generation,session.Token);
            _cloudTranslation=TranslatePhrasesAsync(phrases.Reader,azure,settings,generation,session.Token);
            _translationOverlay.SetMessage("Nemotron → Azure ready ("+worker.Backend+").",3);
        }catch(OperationCanceledException){}catch(Exception ex){App.LogException("translation.start",ex);_translateSpeech=false;RenderTranslationButtons();SetStatus("Translation could not start. Check Nemotron setup and the Azure key/region.");await StopTranslationResourcesAsync();}
        finally{_translationGate.Release();}
    }
    private sealed record PendingPhrase(string Text,string Source,long At);
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
    private async Task TranslatePhrasesAsync(ChannelReader<PendingPhrase> input,AzureTranslatorClient azure,TranslationSettings settings,long generation,CancellationToken ct)
    {
        try {
            await foreach(var phrase in input.ReadAllAsync(ct)) {
                if(System.Diagnostics.Stopwatch.GetElapsedTime(phrase.At).TotalSeconds>8)continue;
                try {
                    string text=await azure.TranslateAsync(phrase.Text,phrase.Source,settings.Target,ct);
                    if(ct.IsCancellationRequested||!_translationGeneration.IsCurrent(generation))return;
                    if(System.Diagnostics.Stopwatch.GetElapsedTime(phrase.At).TotalSeconds<=10)_translationOverlay?.QueueSpeech(StreamingPhrases.Cues(text));
                }catch(OperationCanceledException){throw;}
                catch(Exception ex){App.LogException("translation.azure",ex);SetStatus(ex is InvalidOperationException?ex.Message:"Azure translation failed. Check the connection, key and region.");}
            }
        }catch(OperationCanceledException){}
    }
    private void TranslationFailed(string message){SetStatus(message);_translationOverlay?.SetMessage(message,10);_translateSpeech=false;RenderTranslationButtons();Dispatcher.BeginInvoke(new Action(()=>{if(!_closing)_=RestartTranslationAsync();}));}
    private async Task StopTranslationSessionAsync()
    {
        _translationGeneration.Advance();CancelAndClear(ref _translationSession);var overlay=_translationOverlay;_translationOverlay=null;overlay?.Close();
        await _translationGate.WaitAsync();try{await StopTranslationResourcesAsync();}finally{_translationGate.Release();}
    }
    private async Task StopTranslationResourcesAsync(bool preserveWorker=false)
    {
        bool retained=preserveWorker&&_translationWorker?.CanReuse==true;
        if(!retained&&_translationWorker!=null){await _translationWorker.DisposeAsync();_translationWorker=null;_translationWorkerSettings=null;}
        if(_audioCapture!=null){await _audioCapture.DisposeAsync();_audioCapture=null;}
        try{await Task.WhenAll(_speechTranslation,_cloudTranslation).WaitAsync(TimeSpan.FromSeconds(5));}catch{}
        if(retained&&_translationWorker?.CanReuse!=true&&_translationWorker!=null){await _translationWorker.DisposeAsync();_translationWorker=null;_translationWorkerSettings=null;}
        _speechTranslation=_cloudTranslation=Task.CompletedTask;
    }
}
