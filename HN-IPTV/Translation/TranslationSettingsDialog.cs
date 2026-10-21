using System.Windows;
using System.Windows.Controls;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class TranslationSettingsDialog:Window
{
    private readonly ComboBox _input=new(){IsEditable=true},_output=new(){IsEditable=true},_acceleration=new(),_buffer=new();
    private readonly PasswordBox _azureKey=new();private readonly TextBox _region=new();
    private readonly TextBlock _azureStatus=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBox _log=new(){IsReadOnly=true,Height=150,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly CancellationTokenSource _cancel=new();private bool _working;
    internal TranslationSettings Settings=>new TranslationSettings{InputLanguage=_input.Text,OutputLanguage=_output.Text,Acceleration=_acceleration.SelectedItem as string??"auto",AzureRegion=_region.Text,BufferSeconds=_buffer.SelectedIndex==1?10:_buffer.SelectedIndex==2?15:0}.Normalize();
    internal TranslationSettingsDialog(TranslationSettings settings)
    {
        Title="HN-IPTV — Translation";Width=600;Height=610;MinWidth=480;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var choices=new[]{"auto","en","es","fr","de","pt","it","ar","ja","ko","ru","uk","hi","tr","vi","nl","pl","sv","cs","nb","da","bg","fi","hr","el","hu","ro","sk","sl","et","lv","lt","zh"};
        _input.ItemsSource=choices;_output.ItemsSource=choices;_input.Text=settings.InputLanguage;_output.Text=settings.OutputLanguage;
        _acceleration.ItemsSource=new[]{"auto","cuda","cpu"};_acceleration.SelectedItem=settings.Acceleration;_region.Text=settings.AzureRegion;
        _buffer.ItemsSource=new[]{"Live captions","Generous time — 10 seconds","Generous time — 15 seconds"};_buffer.SelectedIndex=settings.BufferSeconds==10?1:settings.BufferSeconds==15?2:0;
        try{_azureKey.Password=AzureTranslationCredentials.Load();}catch(Exception ex){_azureStatus.Text=ex.Message;}
        var panel=new StackPanel{Margin=new Thickness(20)};var azure=new StackPanel{Margin=new Thickness(20)};
        void Field(StackPanel p,string label,Control control){p.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,10,0,4)});p.Children.Add(control);}
        panel.Children.Add(new TextBlock{Text="Nemotron recognizes speech locally → Azure translates text → timed captions. One multilingual model; no separate language packs. Screen OCR and the old local translators have been retired.",TextWrapping=TextWrapping.Wrap});
        Field(panel,"Input language — Auto detects; manual language code is optional",_input);Field(panel,"Output language — Auto uses Windows display language",_output);Field(panel,"Recognition acceleration",_acceleration);
        Field(panel,"Translation timing (also offered when enabling Translate)",_buffer);
        panel.Children.Add(new TextBlock{Text="Model: Nemotron 3.5 ASR Streaming Q8 (742 MB). Native CPU runtime: 5.5 MB download; optional CUDA runtime: 102 MB. Language accuracy varies; some languages are experimental. Setup validates inference before deleting old Python, Whisper, OCR, translation packs and their CUDA files. Playlists, settings and Azure keys are preserved.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var install=new Button{Content="Download / prepare Nemotron",Margin=new Thickness(0,12,0,8)};panel.Children.Add(install);panel.Children.Add(_log);
        azure.Children.Add(new TextBlock{Text="Azure receives recognized text only. Audio stays on this PC. Your key is encrypted for your Windows account when saved.",TextWrapping=TextWrapping.Wrap});
        Field(azure,"Auth key — Key 1 or Key 2 from Azure Keys and Endpoint",_azureKey);Field(azure,"Region — global, or your resource's region code",_region);
        var test=new Button{Content="Test Azure connection",Margin=new Thickness(0,12,0,8)};azure.Children.Add(test);azure.Children.Add(_azureStatus);
        var tabs=new TabControl();tabs.Items.Add(new TabItem{Header="Speech",Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});tabs.Items.Add(new TabItem{Header="Azure",Content=azure});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};var save=new Button{Content="Save",IsDefault=true,Margin=new Thickness(8)};var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(8)};buttons.Children.Add(cancel);buttons.Children.Add(save);
        var root=new DockPanel();DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);root.Children.Add(tabs);Content=root;
        void Working(bool value){_working=value;save.IsEnabled=install.IsEnabled=test.IsEnabled=!value;}
        save.Click+=(_,_)=>{if(_working)return;try{_=AzureTranslatorClient.NormalizeRegion(_region.Text);if(!string.IsNullOrWhiteSpace(_azureKey.Password))_=new AzureTranslatorClient(_azureKey.Password,_region.Text);AzureTranslationCredentials.Save(_azureKey.Password);DialogResult=true;}catch(Exception ex){_azureStatus.Text=ex.Message;tabs.SelectedIndex=1;}};
        test.Click+=async(_,_)=>{if(_working)return;Working(true);try{var client=new AzureTranslatorClient(_azureKey.Password,_region.Text);_azureStatus.Text="Connected: Hello → "+await client.TranslateAsync("Hello","en","es",_cancel.Token);}catch(OperationCanceledException){}catch(Exception ex){_azureStatus.Text=ex.Message;}finally{Working(false);}};
        install.Click+=async(_,_)=>{if(_working)return;Working(true);try{await LocalTranslationRuntime.InstallAsync(Settings,text=>Dispatcher.BeginInvoke(new Action(()=>{_log.AppendText(text+Environment.NewLine);_log.ScrollToEnd();})),_cancel.Token);}catch(OperationCanceledException){}catch(Exception ex){_log.AppendText(Environment.NewLine+ex.Message);}finally{Working(false);}};
        Closing+=(_,_)=>_cancel.Cancel();
    }
}
