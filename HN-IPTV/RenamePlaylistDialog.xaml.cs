using System.Windows;
namespace HnIptv;
public partial class RenamePlaylistDialog : Window
{
    public string PlaylistName => NameBox.Text.Trim();
    public RenamePlaylistDialog(string name) { InitializeComponent(); NameBox.Text = name; NameBox.SelectAll(); NameBox.Focus(); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PlaylistName.Length == 0) { MessageBox.Show(this, "Enter a name."); return; }
        DialogResult = true;
    }
}
