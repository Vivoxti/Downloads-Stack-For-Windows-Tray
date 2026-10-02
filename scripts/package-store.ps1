# Builds the unsigned x64 MSIX that Partner Center signs after certification. The package identity is not
# invented here: both values must be copied verbatim from Partner Center after reserving the product name.
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()][string]$IdentityName = 'Vivoderin.DownloadsStack',
    [ValidateNotNullOrEmpty()][string]$Publisher = 'CN=FBE5C2B6-BF2B-47A1-ABFA-F14472F4933F',
    [ValidateNotNullOrEmpty()][string]$DisplayName = 'Downloads Stack',
    [string]$PublisherDisplayName = 'Vivoderin',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'DownloadsStack.slnx'
$projectPath = Join-Path $projectRoot 'src/DownloadsStack/DownloadsStack.csproj'
$templatePath = Join-Path $projectRoot 'packaging/store/AppxManifest.xml.in'
$sourceLogo = Join-Path $projectRoot 'src/DownloadsStack/Assets/app.png'
$artifactsPath = Join-Path $projectRoot 'artifacts'
$stagePath = Join-Path $artifactsPath 'store-stage'
$publishPath = Join-Path $stagePath 'content'

$project = [xml](Get-Content -LiteralPath $projectPath)
$version = @($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if (-not $version) { throw 'The project carries no <Version>.' }
$parts = @($version.Split('.') | ForEach-Object { [int]$_ })
if ($parts.Count -gt 4) { throw "Version '$version' has more than four numeric parts." }
while ($parts.Count -lt 4) { $parts += 0 }
if ($parts | Where-Object { $_ -lt 0 -or $_ -gt 65535 }) { throw "Version '$version' has a part outside 0..65535." }
$packageVersion = $parts -join '.'

if (-not $SkipTests) {
    dotnet test $solutionPath -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

if (Test-Path -LiteralPath $stagePath) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null

dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $publishPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$executable = Join-Path $publishPath 'Downloads Stack.exe'
$uiCheck = Start-Process -FilePath $executable -ArgumentList '--check-ui' -WindowStyle Hidden -Wait -PassThru
$uiCheckLog = Join-Path $publishPath 'ui-check.log'
if ($uiCheck.ExitCode -ne 0) {
    if (Test-Path -LiteralPath $uiCheckLog) { Get-Content -LiteralPath $uiCheckLog }
    throw 'Published application resources failed to load.'
}
Remove-Item -LiteralPath $uiCheckLog -Force

# MSIX visual assets have exact pixel sizes. They are deterministic derivatives of the repository logo,
# kept in the staging directory so a logo update cannot leave stale generated files in source control.
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path $publishPath 'Assets'
New-Item -ItemType Directory -Path $assetPath -Force | Out-Null
function Write-SquareLogo([int]$size, [string]$name) {
    $source = [System.Drawing.Image]::FromFile($sourceLogo)
    try {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $ratio = [Math]::Min($size / $source.Width, $size / $source.Height)
                $width = [int][Math]::Round($source.Width * $ratio)
                $height = [int][Math]::Round($source.Height * $ratio)
                $graphics.DrawImage($source, [int](($size - $width) / 2), [int](($size - $height) / 2), $width, $height)
            } finally { $graphics.Dispose() }
            $bitmap.Save((Join-Path $assetPath $name), [System.Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    } finally { $source.Dispose() }
}
Write-SquareLogo 50 'StoreLogo.png'
Write-SquareLogo 44 'Square44x44Logo.png'
Write-SquareLogo 150 'Square150x150Logo.png'

function Escape-Xml([string]$value) { [System.Security.SecurityElement]::Escape($value) }
$manifest = Get-Content -LiteralPath $templatePath -Raw
$manifest = $manifest.Replace('@@IDENTITY_NAME@@', (Escape-Xml $IdentityName))
$manifest = $manifest.Replace('@@PUBLISHER@@', (Escape-Xml $Publisher))
$manifest = $manifest.Replace('@@DISPLAY_NAME@@', (Escape-Xml $DisplayName))
$manifest = $manifest.Replace('@@PUBLISHER_DISPLAY_NAME@@', (Escape-Xml $PublisherDisplayName))
$manifest = $manifest.Replace('@@VERSION@@', $packageVersion)
$manifestPath = Join-Path $publishPath 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

$kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$makeAppx = Get-ChildItem -LiteralPath $kitsRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
    ForEach-Object { Join-Path $_.FullName 'x64/makeappx.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $makeAppx) { throw 'MakeAppx.exe was not found. Install the Windows SDK.' }

$msixPath = Join-Path $artifactsPath "DownloadsStack-$version-store-win-x64.msix"
if (Test-Path -LiteralPath $msixPath) { Remove-Item -LiteralPath $msixPath -Force }
& $makeAppx pack /o /h SHA256 /d $publishPath /p $msixPath
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed.' }

$file = Get-Item -LiteralPath $msixPath
'{0}  {1:N1} MB  {2}' -f $file.Name, ($file.Length / 1MB), (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash
Write-Output 'Unsigned Store package created. Partner Center will sign it after certification.'
Remove-Item -LiteralPath $stagePath -Recurse -Force
