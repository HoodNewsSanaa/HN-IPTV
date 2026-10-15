# Tests installer upgrade mechanics, not the actual historical application binary.
$ErrorActionPreference = 'Stop'
$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$iscc = if ($compiler) { $compiler.Source } else { Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
$directory = Join-Path $PSScriptRoot 'artifacts/upgrade-fixture'
New-Item -ItemType Directory -Force $directory | Out-Null
& $iscc /DAppVersion=0.7.1 /DQuickCompression "/O$directory" /FHN-IPTV-Previous-Setup (Join-Path $PSScriptRoot 'installer/HN-IPTV.iss')
if ($LASTEXITCODE -ne 0) { throw 'Previous-version installer fixture compilation failed.' }
