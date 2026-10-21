using System.IO;
using System.Windows;
using System.Windows.Controls;
using HnIptv.Translation;
namespace HnIptv;

internal sealed class TranslationSettingsDialog : Window
{
    private readonly ComboBox _input = new() { IsEditable = true }, _output = new() { IsEditable = true };
    private readonly ComboBox _model = new(), _acceleration = new(), _overlay = new(), _textModel = new(), _pipeline = new();
    private readonly CheckBox _noise = new() { Content = "Reduce steady background noise (experimental)" };
    private readonly TextBox _packs = new();
    private readonly ComboBox _provider = new();
    private readonly PasswordBox _azureKey = new();
    private readonly TextBox _region = new();
    private readonly TextBlock _azureStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) };
    private readonly TextBox _log = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 130 };
    private readonly CancellationTokenSource _cancel = new();
    private readonly Button _install = new() { Content = "Download / prepare local models", Margin = new Thickness(0, 12, 0, 8) };
    private bool _working;
    internal TranslationSettings Settings => new TranslationSettings {
        InputLanguage = _input.Text, OutputLanguage = _output.Text,
        SpeechModel = _model.SelectedItem as string ?? "auto",
        TextModel = _textModel.SelectedItem as string ?? "auto",
        Provider = _provider.SelectedItem as string ?? "local", AzureRegion = _region.Text,
        SpeechPipeline = _pipeline.SelectedItem as string ?? "auto",
        NoiseSuppression = _noise.IsChecked == true,
        Acceleration = _acceleration.SelectedItem as string ?? "auto",
        ScreenOverlay = _overlay.SelectedItem as string ?? "positioned",
        InstalledInputs = _packs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    }.Normalize();
    internal TranslationSettingsDialog(TranslationSettings settings)
    {
        Title = "HN-IPTV — Translation"; Width = 620; Height = 700;
        ResizeMode = ResizeMode.CanResize; MinWidth = 500; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var choices = new[] { "auto", "en", "es", "fr", "de", "pt", "it", "ja", "ko", "zh", "zt", "ru", "uk", "ar", "hi", "tr", "nl", "pl" };
        _input.ItemsSource = choices; _output.ItemsSource = choices;
        _input.Text = settings.InputLanguage; _output.Text = settings.OutputLanguage;
        _model.ItemsSource = new[] { "auto", "tiny", "base", "small", "medium", "turbo" }; _model.SelectedItem = settings.SpeechModel;
        _acceleration.ItemsSource = new[] { "auto", "cuda", "cpu" }; _acceleration.SelectedItem = settings.Acceleration;
        _overlay.ItemsSource = new[] { "positioned", "captions" }; _overlay.SelectedItem = settings.ScreenOverlay;
        _textModel.ItemsSource = new[] { "auto", "argos" }; _textModel.SelectedItem = settings.TextModel;
        _pipeline.ItemsSource = new[] { "auto", "direct", "staged" }; _pipeline.SelectedItem = settings.SpeechPipeline;
        _noise.IsChecked = settings.NoiseSuppression;
        _provider.ItemsSource = new[] { "local", "azure" }; _provider.SelectedItem = settings.Provider;
        _region.Text = settings.AzureRegion;
        try { _azureKey.Password = AzureTranslationCredentials.Load(); }
        catch (Exception ex) { _azureStatus.Text = ex.Message; }
        _packs.Text = string.Join(", ", settings.InstalledInputs);
        void Field(string label, Control control) {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(control);
        }
        panel.Children.Add(new TextBlock { Text = "Speech recognition and screen OCR run on this PC. Local translation stays offline. Selecting Azure sends recognized text for online translation.",
            TextWrapping = TextWrapping.Wrap });
        Field("Input language — Auto detects speech/text; or enter a language code", _input);
        Field("Output language — Auto uses your Windows display language", _output);
        Field("Speech model — auto selects the recommended prepared model; manual choices remain available", _model);
        Field("Hardware acceleration — auto uses NVIDIA CUDA when prepared; cpu avoids GPU downloads", _acceleration);
        panel.Children.Add(new TextBlock { Text = "Auto prepares turbo on NVIDIA CUDA, small on CPU. Turbo recognizes speech then uses a separate translator. Existing manual model selections are preserved. CUDA libraries stay cached; GPU failures fall back to CPU.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        Field("Speech pipeline — auto; direct English; staged recognition then translation", _pipeline);
        Field("Text model — auto uses OPUS-MT for Arabic to English; Argos for other pairs", _textModel);
        panel.Children.Add(_noise);
        Field("Screen translation — positioned covers the original text; captions uses a separate bar", _overlay);
        Field("Input language packs to install (comma-separated codes)", _packs);
        panel.Children.Add(new TextBlock { Text = "Prepare packs for the languages you watch. Auto screen detection uses installed packs. Extra runtime and models can require several GB; they stay cached across updates.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        panel.Children.Add(_install); panel.Children.Add(_log);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var tabs = new TabControl();
        var save = new Button { Content = "Save", IsDefault = true, Margin = new Thickness(5) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(5) };
        save.Click += (_, _) => {
            if (_working) return;
            try {
                var requested = Settings;
                _ = AzureTranslatorClient.NormalizeRegion(requested.AzureRegion);
                if (requested.Provider == "azure")
                    _ = new AzureTranslatorClient(_azureKey.Password,requested.AzureRegion);
                AzureTranslationCredentials.Save(_azureKey.Password);
                DialogResult = true;
            }
            catch (Exception ex) { _azureStatus.Text = ex.Message; tabs.SelectedIndex = 1; }
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var azurePanel = new StackPanel { Margin = new Thickness(20) };
        void AzureField(string label, Control control) {
            azurePanel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,10,0,4) });
            azurePanel.Children.Add(control);
        }
        azurePanel.Children.Add(new TextBlock { Text = "Azure Translator is optional. It receives only confirmed speech text and recognized screen text, never audio or video. Choose local for offline translation. The auth key is encrypted for your Windows account when you Save.",
            TextWrapping = TextWrapping.Wrap });
        AzureField("Translation provider",_provider);
        AzureField("Auth key — paste Key 1 or Key 2 from Azure Keys and Endpoint",_azureKey);
        AzureField("Region — global, or the exact Azure region code such as eastus",_region);
        azurePanel.Children.Add(new TextBlock { Text = "Endpoint: https://api.cognitive.microsofttranslator.com\nUse a public Azure Translator resource. The F0 free tier and its quota are managed in Azure. This app does not change your Azure pricing tier.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) });
        var test = new Button { Content = "Test Azure connection", Margin = new Thickness(0,12,0,0), HorizontalAlignment = HorizontalAlignment.Left };
        test.Click += async (_, _) => {
            if (_working) return;
            _working = true; test.IsEnabled = false; save.IsEnabled = false; _install.IsEnabled = false;
            _azureStatus.Text = "Testing with the word Hello (five source characters)...";
            try {
                var client = new AzureTranslatorClient(_azureKey.Password,_region.Text);
                var translated = await client.TranslateAsync("Hello","en","es",_cancel.Token);
                _azureStatus.Text = "Connected. Hello → "+translated+". Save with provider azure to use it.";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _azureStatus.Text = ex.Message; }
            finally { _working = false; test.IsEnabled = true; save.IsEnabled = true; _install.IsEnabled = true; }
        };
        azurePanel.Children.Add(test);
        var clear = new Button { Content = "Clear key field (applied on Save)", Margin = new Thickness(0,8,0,0), HorizontalAlignment = HorizontalAlignment.Left };
        clear.Click += (_, _) => { _azureKey.Clear(); _provider.SelectedItem = "local"; _azureStatus.Text = "Save to remove the stored key and use local translation."; };
        azurePanel.Children.Add(clear); azurePanel.Children.Add(_azureStatus);
        tabs.Items.Add(new TabItem { Header = "Translation", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        tabs.Items.Add(new TabItem { Header = "Azure", Content = new ScrollViewer { Content = azurePanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var root = new DockPanel(); DockPanel.SetDock(buttons,Dock.Bottom); root.Children.Add(buttons); root.Children.Add(tabs); Content = root;
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
