using System.Windows;
using System.Windows.Controls;
namespace HnIptv;

internal sealed class SourceDialog : Window
{
    private readonly TextBox _source;
    public string Source => _source.Text.Trim();
    public SourceDialog(string title, string description, string value = "")
    {
        Title = title; Width = 560; Height = 190; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        _source = new TextBox { Text = value, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(_source);
        var accept = new Button { Content = "Save", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right };
        accept.Click += (_, _) => { DialogResult = true; };
        panel.Children.Add(accept); Content = panel;
        Loaded += (_, _) => { _source.Focus(); _source.SelectAll(); };
    }
}
