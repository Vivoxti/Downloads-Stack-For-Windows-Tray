# The two things a release is made of: a portable archive to unpack anywhere, and a per-user installer.
# Both carry the same self-contained build, and starting with Windows works the same way in both — the
# application writes its own entry under the user's Run key, from wherever it is.
#
# They come from two publishes, because the portable one is a single executable and the installer needs
# the ordinary folder. Measured, interleaved, on this machine: the bundle costs nothing to load (1020 ms
# against the folder's 1024 ms), and it is the same 140 MB either way, so the archive stays about 61 MB.
# EnableCompressionInSingleFile was tried and dropped: 67 MB as a bare file, but 2028 ms to load, every
# launch and not just the first — a bad trade for something that starts at sign-in.
#
# The installer asks for no administrator rights and installs under %LOCALAPPDATA%\Programs: the tray
# application belongs to one user, its startup entry is that user's, and it writes a shortcut next to its
# own executable, which a folder under Program Files would forbid.
[CmdletBinding()]
param([switch]$SkipTests, [switch]$SkipInstaller)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'DownloadsStack.slnx'
$projectPath = Join-Path $projectRoot 'src/DownloadsStack/DownloadsStack.csproj'
$artifactsPath = Join-Path $projectRoot 'artifacts'
$stagePath = Join-Path $artifactsPath 'stage'
$appPath = Join-Path $stagePath 'app'
$singlePath = Join-Path $stagePath 'single'

$version = @(([xml](Get-Content -LiteralPath $projectPath)).Project.PropertyGroup |
    ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'The project carries no <Version>.' }
Write-Output "Downloads Stack $version"

if (-not $SkipTests) {
    dotnet test $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# A stage of its own, emptied first: the archive and the installer must carry the application and
# nothing else. artifacts/win-x64 from publish.ps1 also holds the documents and the check's own log.
if (Test-Path -LiteralPath $stagePath) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $appPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $appPath 'Downloads Stack.exe'
$uiCheck = Start-Process -FilePath $executable -ArgumentList '--check-ui' -WindowStyle Hidden -Wait -PassThru
$uiCheckLog = Join-Path $appPath 'ui-check.log'
if ($uiCheck.ExitCode -ne 0) {
    Get-Content -LiteralPath $uiCheckLog
    throw 'Published application resources failed to load.'
}
Remove-Item -LiteralPath $uiCheckLog -Force # Written by the check above, not part of the application.

$outputs = @()

if (-not $SkipInstaller) {
    # WiX is a local tool of this repository: restoring it is the whole of the installer toolchain.
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Restoring the WiX tool failed.' }
    $msiPath = Join-Path $artifactsPath "DownloadsStack-$version-win-x64.msi"
    Write-Output 'Building the installer (compressing ~140 MB into a cabinet takes a few minutes)...'
    dotnet wix build (Join-Path $projectRoot 'packaging/DownloadsStack.wxs') `
        -arch x64 -d "Version=$version" -d "PublishDir=$appPath" -o $msiPath
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    $outputs += $msiPath
}

# The portable build is one executable, so unpacking the archive puts a single file wherever it is
# unpacked instead of scattering 400. The shortcut the application writes next to itself takes its icon
# from the executable, so nothing has to travel alongside it.
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $singlePath
if ($LASTEXITCODE -ne 0) { throw 'Publishing the single file failed.' }

$singleExecutable = Join-Path $singlePath 'Downloads Stack.exe'
$singleCheck = Start-Process -FilePath $singleExecutable -ArgumentList '--check-ui' -WindowStyle Hidden -Wait -PassThru
if ($singleCheck.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $singlePath 'ui-check.log')
    throw 'The single-file application failed to load its resources.'
}

$zipPath = Join-Path $artifactsPath "DownloadsStack-$version-portable-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression # ZipArchiveMode lives here, the rest next door.
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Not Compress-Archive: it takes minutes over a file this size and this takes seconds.
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $archive, $singleExecutable, 'Downloads Stack.exe',
        [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
} finally { $archive.Dispose() }
$outputs += $zipPath

Remove-Item -LiteralPath $stagePath -Recurse -Force
foreach ($output in $outputs) {
    $file = Get-Item -LiteralPath $output
    '{0}  {1:N1} MB  {2}' -f $file.Name, ($file.Length / 1MB), (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
}
