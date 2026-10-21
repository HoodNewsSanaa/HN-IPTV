using System.Windows;
namespace HnIptv;

public partial class MainWindow
{
    private void Mute_Click(object sender, RoutedEventArgs e) => ToggleMute();
    private void RenderToggleStates()
    {
        if (!IsInitialized || MuteButton == null) return;
        ToggleVisual.SetIsActive(CaptionsButton, _playerSettings.CaptionsEnabled);
        ToggleVisual.SetIsActive(TranslateSpeechButton, _translateSpeech);
        ToggleVisual.SetIsActive(TranslateScreenButton, _translateScreen);
        ToggleVisual.SetIsActive(StreamInfoButton, (string)StreamInfoButton.Content == "Hide info");
        ToggleVisual.SetIsActive(PauseButton, _userPaused);
        ToggleVisual.SetIsActive(MuteButton, _muted);
        ToggleVisual.SetIsActive(FullscreenButton, _isFullScreen);
        ToggleVisual.SetIsActive(PipButton, _pip != null);
        ToggleVisual.SetIsActive(FavoriteButton, _selectedChannel?.IsFavorite == true);
        ToggleVisual.SetIsActive(GroupSearchButton, GroupSearchBox.Visibility == Visibility.Visible);
        ToggleVisual.SetIsActive(ChannelSearchButton, SearchBox.Visibility == Visibility.Visible);
        MuteButton.Content = _muted ? "Unmute" : "Mute";
        _fullscreenControls?.SetToggleStates(_playerSettings.CaptionsEnabled, _translateSpeech, _translateScreen, _muted, _userPaused);
        _pip?.SetPlaybackState(_userPaused, _muted);
    }
}
