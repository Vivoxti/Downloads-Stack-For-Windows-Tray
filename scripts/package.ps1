# The two things a release is made of, from one publish: a portable archive to unpack anywhere, and a
# per-user installer. Both carry the same self-contained build, and starting with Windows works the same
# way in both — the application writes its own entry under the user's Run key, from wherever it is.
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
$portablePath = Join-Path $stagePath 'portable'

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

# The archive carries one folder, so unpacking it into a downloads folder does not scatter 400 files.
$portableRoot = Join-Path $portablePath 'Downloads Stack'
New-Item -ItemType Directory -Path $portableRoot -Force | Out-Null
Copy-Item -Path (Join-Path $appPath '*') -Destination $portableRoot -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $portableRoot
$zipPath = Join-Path $artifactsPath "DownloadsStack-$version-portable-win-x64.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression # ZipArchiveMode lives here, the rest next door.
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Not Compress-Archive: it takes minutes over hundreds of megabytes and this takes seconds. Entry by
# entry rather than CreateFromDirectory, because under Windows PowerShell that writes backslashes into
# the names, which is not what the format says and not what every unpacker reads back as folders.
$root = (Get-Item -LiteralPath $portablePath).FullName
$archive = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
        $entry = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$outputs += $zipPath

Remove-Item -LiteralPath $stagePath -Recurse -Force
foreach ($output in $outputs) {
    $file = Get-Item -LiteralPath $output
    '{0}  {1:N1} MB  {2}' -f $file.Name, ($file.Length / 1MB), (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash
}
