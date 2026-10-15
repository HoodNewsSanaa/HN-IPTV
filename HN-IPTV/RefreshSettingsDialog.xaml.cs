using System.Windows;
using HnIptv.Services;

namespace HnIptv;
public partial class RefreshSettingsDialog : Window
{
    public int ChannelHours { get; private set; }
    public int EpgMinutes { get; private set; }
    public int LogoHours { get; private set; }
    public RefreshSettingsDialog(PlaylistProfile playlist)
    {
        InitializeComponent();
        ChannelsBox.Text = playlist.ChannelRefreshHours.ToString();
        EpgBox.Text = playlist.EpgRefreshMinutes.ToString();
        LogosBox.Text = playlist.LogoRefreshHours.ToString();
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ChannelsBox.Text, out var channels) || channels is < 1 or > 168 ||
            !int.TryParse(EpgBox.Text, out var epg) || epg is < 5 or > 1440 ||
            !int.TryParse(LogosBox.Text, out var logos) || logos is < 1 or > 168)
        {
            MessageBox.Show(this, "Use 1–168 hours for channels/logos and 5–1440 minutes for EPG."); return;
        }
        ChannelHours = channels; EpgMinutes = epg; LogoHours = logos;
        DialogResult = true;
    }
}
