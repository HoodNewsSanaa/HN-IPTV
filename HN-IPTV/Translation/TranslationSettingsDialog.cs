using System.IO;
using System.Windows;
using System.Windows.Controls;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class TranslationSettingsDialog : Window
{
    private readonly ComboBox _input = new() { IsEditable = true }, _output = new() { IsEditable = true };
    private readonly ComboBox _model = new();
    private readonly TextBox _packs = new();
    private readonly TextBox _log = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 130 };
    private readonly CancellationTokenSource _cancel = new();
    private readonly Button _install = new() { Content = "Download / prepare local models", Margin = new Thickness(0, 12, 0, 8) };
    private bool _working;
    internal TranslationSettings Settings => new TranslationSettings {
        InputLanguage = _input.Text, OutputLanguage = _output.Text,
        SpeechModel = _model.SelectedItem as string ?? "base",
        InstalledInputs = _packs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    }.Normalize();
    internal TranslationSettingsDialog(TranslationSettings settings)
    {
        Title = "HN-IPTV — Local translation"; Width = 600; Height = 600;
        ResizeMode = ResizeMode.CanResize; MinWidth = 500; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var choices = new[] { "auto", "en", "es", "fr", "de", "pt", "it", "ja", "ko", "zh", "zt", "ru", "uk", "ar", "hi", "tr", "nl", "pl" };
        _input.ItemsSource = choices; _output.ItemsSource = choices;
        _input.Text = settings.InputLanguage; _output.Text = settings.OutputLanguage;
        _model.ItemsSource = new[] { "tiny", "base", "small" }; _model.SelectedItem = settings.SpeechModel;
        _packs.Text = string.Join(", ", settings.InstalledInputs);
        void Field(string label, Control control) {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(control);
        }
        panel.Children.Add(new TextBlock { Text = "Translation runs on this PC. Setup downloads the runtime and models once; playback translation has network access disabled.",
            TextWrapping = TextWrapping.Wrap });
        Field("Input language — Auto detects speech/text; or enter a language code", _input);
        Field("Output language — Auto uses your Windows display language", _output);
        Field("Speech model — tiny is faster, small uses more resources", _model);
        Field("Input language packs to install (comma-separated codes)", _packs);
        panel.Children.Add(new TextBlock { Text = "Prepare packs for the languages you watch. Auto screen detection uses installed packs. Extra runtime and models can require several GB; they stay cached across updates.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_install); panel.Children.Add(_log);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "Save", IsDefault = true, Margin = new Thickness(5) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(5) };
        save.Click += (_, _) => { if (!_working) DialogResult = true; };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _install.Click += async (_, _) => {
            if (_working) return;
            _working = true; _install.IsEnabled = false; save.IsEnabled = false;
            try {
                var requested = Settings;
                if (requested.InstalledInputs.Length == 0) throw new InvalidDataException("Choose at least one input language pack.");
                await LocalTranslationRuntime.InstallAsync(requested, text => Dispatcher.BeginInvoke(new Action(() => {
                    _log.AppendText(text + Environment.NewLine);
                    if (_log.Text.Length > 16000) _log.Text = _log.Text[^12000..];
                    _log.ScrollToEnd();
                })), _cancel.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log.AppendText(Environment.NewLine + ex.Message); }
            finally { _working = false; _install.IsEnabled = true; save.IsEnabled = true; }
        };
        Closing += (_, _) => _cancel.Cancel();
    }
}
