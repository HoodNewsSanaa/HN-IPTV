using System.Windows;
using System.Windows.Controls;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class TranslationSettingsDialog:Window
{
    private readonly ComboBox _input=new(){IsEditable=true},_acceleration=new(),_buffer=new();
    private readonly TextBox _log=new(){IsReadOnly=true,Height=150,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    private readonly CancellationTokenSource _cancel=new();
    private bool _working;
    internal TranslationSettings Settings=>new TranslationSettings{InputLanguage=_input.Text,Acceleration=_acceleration.SelectedItem as string??"auto",BufferSeconds=_buffer.SelectedIndex==1?10:_buffer.SelectedIndex==2?15:0}.Normalize();
    internal TranslationSettingsDialog(TranslationSettings settings)
    {
        Title="HN-IPTV - Whisper settings";Width=600;Height=540;MinWidth=480;MinHeight=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        _input.ItemsSource=new[]{"auto","en","es","fr","de","pt","it","ar","ja","ko","ru","uk","hi","tr","vi","nl","pl","zh"};_input.Text=settings.InputLanguage;
        _acceleration.ItemsSource=new[]{"auto","vulkan","cpu"};_acceleration.SelectedItem=settings.Acceleration;
        _buffer.ItemsSource=new[]{"Live captions","Generous time - 10 seconds","Generous time - 15 seconds"};_buffer.SelectedIndex=settings.BufferSeconds==10?1:settings.BufferSeconds==15?2:0;
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Whisper translates speech directly into English on this device. No account or API key is needed. The Info panel shows detected input language and the latest English output.",TextWrapping=TextWrapping.Wrap});
        void Field(string label,Control control){panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,10,0,4)});panel.Children.Add(control);}
        Field("Input language - Auto detects; manual code is optional",_input);Field("Acceleration",_acceleration);Field("Translation timing",_buffer);
        panel.Children.Add(new TextBlock{Text="Whisper Medium Q5: 539 MB once, plus the 0.9 MB speech detector. Existing model and media transport downloads are reused. Output: English. GPU acceleration is recommended.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)});
        var install=new Button{Content="Download / prepare Whisper",Margin=new Thickness(0,12,0,8)};panel.Children.Add(install);panel.Children.Add(_log);
        var save=new Button{Content="Save",IsDefault=true,Margin=new Thickness(8)};var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(8)};
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};buttons.Children.Add(cancel);buttons.Children.Add(save);
        var root=new DockPanel();DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);root.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=root;
        save.Click+=(_,_)=>{if(!_working)DialogResult=true;};
        install.Click+=async(_,_)=>{if(_working)return;_working=true;save.IsEnabled=install.IsEnabled=false;try{await LocalTranslationRuntime.InstallAsync(Settings,text=>Dispatcher.BeginInvoke(new Action(()=>{_log.AppendText(text+Environment.NewLine);_log.ScrollToEnd();})),_cancel.Token);}catch(OperationCanceledException){}catch(Exception ex){_log.AppendText(Environment.NewLine+ex.Message);}finally{_working=false;save.IsEnabled=install.IsEnabled=true;}};
        Closing+=(_,_)=>_cancel.Cancel();
    }
}
