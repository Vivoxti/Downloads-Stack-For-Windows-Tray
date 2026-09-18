$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'DownloadsStack.slnx'
$projectPath = Join-Path $projectRoot 'src/DownloadsStack/DownloadsStack.csproj'
$outputPath = Join-Path $projectRoot 'artifacts/win-x64'
dotnet test $solutionPath -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
$uiCheck = Start-Process -FilePath (Join-Path $outputPath 'Downloads Stack.exe') -ArgumentList '--check-ui' -WindowStyle Hidden -Wait -PassThru
if ($uiCheck.ExitCode -ne 0) {
    Get-Content -LiteralPath (Join-Path $outputPath 'ui-check.log')
    throw 'Published application resources failed to load.'
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'CHECKS.md') -Destination $outputPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'SPEC.md') -Destination $outputPath
Write-Output "Build: $outputPath"
