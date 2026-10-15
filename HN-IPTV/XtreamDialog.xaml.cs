using System.Windows;
using HnIptv.Services;

namespace HnIptv;

public partial class XtreamDialog : Window
{
    public string ServerUrl => ServerBox.Text.Trim();
    public string Username => UsernameBox.Text.Trim();
    public string Password => PasswordBox.Password;
    public XtreamStreamFormat StreamFormat => FormatBox.SelectedIndex switch
    {
        1 => XtreamStreamFormat.MpegTs,
        2 => XtreamStreamFormat.Hls,
        _ => XtreamStreamFormat.Automatic
    };

    public XtreamDialog() => InitializeComponent();

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            MessageBox.Show(this, "Enter the server URL, username and password.", "Missing information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
