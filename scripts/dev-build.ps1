# A single executable in the project root, for checking a change by hand. Self-contained: framework-dependent
# single-file crashes on startup here (0xC000041D before a line of ours runs), and one file that just runs is
# the whole point. ReadyToRun is off - it doubles the build and this is not the executable to measure startup
# on anyway, since a single file also extracts its native libraries on first run. The release build is
# publish.ps1: ReadyToRun, tested, and it stays out of the root.
[CmdletBinding()]
param([switch]$Run)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src/DownloadsStack/DownloadsStack.csproj'
$stagePath = Join-Path $projectRoot 'src/DownloadsStack/obj/dev-build'
$executable = Join-Path $projectRoot 'Downloads Stack.exe'

# Publishing straight into the root would leave its scratch files there, and a locked executable would
# fail halfway through. Stage it, then replace the one file.
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=false -o $stagePath --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$running = Get-Process -Name 'Downloads Stack' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $executable }
if ($running) {
    throw "The build in the project root is running (pid $($running.Id -join ', ')). Exit it from the tray and run this again."
}

Copy-Item -LiteralPath (Join-Path $stagePath 'Downloads Stack.exe') -Destination $executable -Force
# The shortcut the application writes next to itself takes its icon from this file.
Copy-Item -LiteralPath (Join-Path $projectRoot 'src/DownloadsStack/Assets/app.ico') -Destination (Join-Path $projectRoot 'DownloadsStack.App.ico') -Force

$stamp = (Get-Item -LiteralPath $executable).LastWriteTime.ToString('HH:mm:ss')
Write-Output "Built: $executable ($stamp)"
if ($Run) { Start-Process -FilePath $executable }
