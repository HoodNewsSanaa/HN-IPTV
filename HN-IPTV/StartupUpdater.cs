using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HnIptv.Services;
namespace HnIptv;

internal sealed class StartupUpdater:Window
{
    private readonly CancellationTokenSource _skip=new();
    private readonly TextBlock _status=new(){Text="Checking for updates...",Margin=new Thickness(0,12,0,12),TextWrapping=TextWrapping.Wrap};
    private readonly ProgressBar _progress=new(){IsIndeterminate=true,Height=6};
    private readonly Button _continue=new(){Content="Continue without updating",Margin=new Thickness(0,16,0,0)};
    internal StartupUpdater()
    {
        Title="HN-IPTV";Width=430;Height=210;ResizeMode=ResizeMode.NoResize;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(new TextBlock{Text="HN-IPTV",FontSize=22,FontWeight=FontWeights.Bold});panel.Children.Add(_status);panel.Children.Add(_progress);panel.Children.Add(_continue);Content=panel;
        _continue.Click+=(_,_)=>_skip.Cancel();Closing+=(_,_)=>_skip.Cancel();
    }
    internal static async Task<bool> RunAsync()
    {
        var window=new StartupUpdater();window.Show();
        try {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(6)};
            var release=await new GitHubUpdates(http).CheckAsync(new Version(BuildInfo.Version),AppStorage.IsPortable,window._skip.Token,featureRich:true);
            // Automatic execution requires GitHub's asset digest as well as matching release-note SHA-256.
            if(release==null||!release.HasAssetDigest)return false;
            string failed=Path.Combine(AppStorage.DataDirectory,"startup-update-failed.json");
            if(File.Exists(failed))try {
                using var json=JsonDocument.Parse(File.ReadAllText(failed));
                if(json.RootElement.GetProperty("version").GetString()==release.Version.ToString()&&DateTimeOffset.Parse(json.RootElement.GetProperty("retryAfter").GetString()!)>DateTimeOffset.UtcNow)return false;
            }catch(Exception ex)when(ex is JsonException or FormatException or IOException or KeyNotFoundException){}
            window._status.Text="Downloading HN-IPTV "+release.Version+"...";
            string prepared=await AppUpdater.PrepareAsync(release,AppStorage.IsPortable,window._skip.Token,value=>{window._progress.IsIndeterminate=!double.IsFinite(value);if(double.IsFinite(value))window._progress.Value=value;});
            window._skip.Token.ThrowIfCancellationRequested();window._continue.IsEnabled=false;window._status.Text="Installing verified update...";
            AppUpdater.StartAutomatic(prepared,AppStorage.IsPortable,release);return true;
        }catch(OperationCanceledException){return false;}
        catch(Exception ex){App.LogException("update.startup",ex);return false;}
        finally{window.Close();window._skip.Dispose();}
    }
}
