using System.Windows;
using System.Windows.Controls;
namespace HnIptv;

internal sealed class TranslationModeDialog:Window
{
    internal int BufferSeconds{get;private set;}
    internal TranslationModeDialog(int previous,bool bufferedSupported)
    {
        Title="Translate speech";Width=470;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel{Margin=new Thickness(20)};
        panel.Children.Add(new TextBlock{Text="Choose how much time to give translation.",Margin=new Thickness(0,0,0,12)});
        foreach(var choice in new[]{(0,"Live — captions follow speech"),(10,"Generous time — 10-second buffer"),(15,"Generous time — 15-second buffer (recommended)")}) {
            var radio=new RadioButton{Content=choice.Item2,Margin=new Thickness(0,6,0,6),IsChecked=(bufferedSupported?previous:0)==choice.Item1,IsEnabled=choice.Item1==0||bufferedSupported};
            radio.Checked+=(_,_)=>BufferSeconds=choice.Item1;panel.Children.Add(radio);
        }
        BufferSeconds=bufferedSupported?previous:0;
        panel.Children.Add(new TextBlock{Text="Generous time keeps audio and video together and prepares longer phrases ahead of playback. Playback waits if translation needs more time. Its media transport downloads once (115 MB).",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,14)});
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="Cancel",IsCancel=true,Margin=new Thickness(5)};var start=new Button{Content="Start translation",IsDefault=true,Margin=new Thickness(5)};start.Click+=(_,_)=>DialogResult=true;
        buttons.Children.Add(cancel);buttons.Children.Add(start);panel.Children.Add(buttons);Content=panel;
    }
}
