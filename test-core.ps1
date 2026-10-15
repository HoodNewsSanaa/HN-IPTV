$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet run --project (Join-Path $PSScriptRoot "HN-IPTV.Core.SmokeTests/HN-IPTV.Core.SmokeTests.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Shared Core checks failed." }

# WPF RangeBase.Value (used by ProgressBar) has two-way binding semantics by default.
# A display-only binding to Channel.EpgProgress has a private setter, and silently
# relying on the default mode causes an InvalidOperationException on row creation.
# Keep every ProgressBar Value binding explicitly one-way to catch this regression.
[xml]$mainXaml = Get-Content (Join-Path $PSScriptRoot "HN-IPTV/MainWindow.xaml") -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($mainXaml.NameTable)
$ns.AddNamespace("wpf", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
$progressBars = $mainXaml.SelectNodes("//wpf:ProgressBar[@Value]", $ns)
foreach ($bar in $progressBars) {
    $binding = $bar.GetAttribute("Value")
    if ($binding -match '^\{Binding\b' -and $binding -notmatch '\bMode\s*=\s*OneWay\b') {
        throw "Unsafe ProgressBar.Value binding in MainWindow.xaml: $binding. Set Mode=OneWay."
    }
}
Write-Host "WPF ProgressBar binding safety check passed."
