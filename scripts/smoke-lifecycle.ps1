param([string]$Executable = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/win-x64/Downloads Stack.exe'))
$ErrorActionPreference = 'Stop'
$processName = [System.IO.Path]::GetFileNameWithoutExtension($Executable)
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw 'Close Downloads Stack before running the lifecycle smoke check.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DownloadsStackProbe {
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfoW(uint action, uint parameter, out Rect rect, uint update);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    // The flyout is the only window this process ever shows; the tray icon host stays hidden.
    public static IntPtr FindVisibleWindow(int processId) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)processId && IsWindowVisible(window)) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
    public static Rect WorkArea() { Rect area; SystemParametersInfoW(0x0030, 0, out area, 0); return area; }
}
'@
function Wait-For([scriptblock]$Condition, [int]$Seconds = 8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}
$logPath = Join-Path $env:LOCALAPPDATA 'DownloadsStack\app.log'
$logBefore = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }
$primary = $null
$secondary = $null
try {
    $primary = Start-Process -FilePath $Executable -PassThru
    Start-Sleep -Seconds 3
    $primary.Refresh()
    if ($primary.HasExited) { throw "The primary instance exited with $($primary.ExitCode)." }
    # A tray application must not put anything on screen until it is asked to.
    $startsHidden = ([DownloadsStackProbe]::FindVisibleWindow($primary.Id) -eq [IntPtr]::Zero)
    # A second launch has to reach the running instance over its pipe and open the list there.
    $secondary = Start-Process -FilePath $Executable -PassThru
    $secondExited = $secondary.WaitForExit(10000)
    $shown = Wait-For { [DownloadsStackProbe]::FindVisibleWindow($primary.Id) -ne [IntPtr]::Zero }
    $windowHandle = [DownloadsStackProbe]::FindVisibleWindow($primary.Id)
    if ($windowHandle -eq [IntPtr]::Zero) { throw 'The second launch did not open the list on the running instance.' }
    $extendedStyle = [DownloadsStackProbe]::GetWindowLongPtr($windowHandle, -20).ToInt64()
    $rect = [DownloadsStackProbe+Rect]::new()
    [DownloadsStackProbe]::GetWindowRect($windowHandle, [ref]$rect) | Out-Null
    $work = [DownloadsStackProbe]::WorkArea()
    $result = [IntPtr]::Zero
    # Closing the window is a request to go back to the tray, never a request to quit.
    [DownloadsStackProbe]::SendMessageTimeoutW($windowHandle, 0x0010, [IntPtr]0, [IntPtr]0, 2, 3000, [ref]$result) | Out-Null
    $hidden = Wait-For { -not [DownloadsStackProbe]::IsWindowVisible($windowHandle) } 4
    $primary.Refresh()
    $instances = @(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count
    $shortcutPath = Join-Path (Split-Path -Parent $Executable) 'Downloads Stack.lnk'
    $shortcutAppId = $null
    $shell = $null; $folder = $null; $shortcut = $null
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path -Parent $Executable))
        $shortcut = $folder.ParseName('Downloads Stack.lnk')
        if ($null -ne $shortcut) { $shortcutAppId = $shortcut.ExtendedProperty('System.AppUserModel.ID') }
    } finally {
        foreach ($comObject in @($shortcut, $folder, $shell)) {
            if ($null -ne $comObject) { [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($comObject) | Out-Null }
        }
    }
    $report = [ordered]@{
        startsHiddenInTray = $startsHidden
        secondaryExited = $secondExited
        secondaryExitCode = $(if ($secondExited) { $secondary.ExitCode } else { $null })
        listOpenedBySecondLaunch = $shown
        # WPF keeps ShowInTaskbar="False" windows off the taskbar by parking them on a hidden owner,
        # so an owner is expected here; only WS_EX_APPWINDOW would put the flyout back on the taskbar.
        keptOutOfTaskbar = (($extendedStyle -band 0x40000) -eq 0 -and
            (($extendedStyle -band 0x80) -ne 0 -or [DownloadsStackProbe]::GetWindow($windowHandle, 4) -ne [IntPtr]::Zero))
        extendedStyle = ('0x{0:X}' -f $extendedStyle)
        widthPixels = $rect.Right - $rect.Left
        heightPixels = $rect.Bottom - $rect.Top
        anchoredAboveTaskbar = ($rect.Bottom -le $work.Bottom -and $rect.Bottom -gt $work.Top -and $rect.Left -ge $work.Left -and $rect.Right -le $work.Right)
        closeHidesToTray = $hidden
        survivesClose = (-not $primary.HasExited -and [DownloadsStackProbe]::IsWindow($windowHandle))
        instanceCount = $instances
        shortcutCreated = (Test-Path -LiteralPath $shortcutPath)
        shortcutAppId = $shortcutAppId
    }
    $failures = @($report.Keys | Where-Object { $report[$_] -is [bool] -and -not $report[$_] })
    if ($secondary.ExitCode -ne 0) { $failures += 'secondaryExitCode' }
    if ($instances -ne 1) { $failures += 'instanceCount' }
    if ($shortcutAppId -ne 'Vivoderin.DownloadsStack') { $failures += 'shortcutAppId' }
    if ($report.widthPixels -le 0 -or $report.heightPixels -le 0) { $failures += 'windowGeometry' }
    if ($failures.Count -gt 0) { throw (([ordered]@{ failed = $failures; report = $report }) | ConvertTo-Json -Depth 4) }
    $primary.Kill()
    $report.exitsOnRequest = $primary.WaitForExit(8000)
    if (-not $report.exitsOnRequest) { throw 'The process did not terminate.' }
    Start-Sleep -Milliseconds 400
    $logAfter = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }
    # Anything logged during a plain start, show and close is a defect worth reading before release.
    $report.loggedBytesDuringRun = $logAfter - $logBefore
    $report | ConvertTo-Json
} finally {
    foreach ($ownedProcess in @($secondary, $primary)) {
        if ($null -ne $ownedProcess -and -not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id -Force }
    }
}
