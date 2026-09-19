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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, System.Text.StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    // The tray lives on its own hidden window now that the icon is registered through Shell_NotifyIcon
    // directly. Finding it is what lets this check deliver a real tray click.
    public static IntPtr FindWindowByTitle(int processId, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, parameter) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)processId) { return true; }
            var text = new System.Text.StringBuilder(128);
            GetWindowTextW(window, text, text.Capacity);
            if (text.ToString() == title) { found = window; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
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
    // SystemParametersInfo only ever describes the primary display. The flyout opens on the monitor under
    // the pointer, so on a second screen the primary work area says nothing about where it belongs.
    public static Rect WorkArea(IntPtr window) {
        var info = new MonitorInfo(); info.Size = (uint)Marshal.SizeOf<MonitorInfo>();
        IntPtr monitor = MonitorFromWindow(window, 2); // MONITOR_DEFAULTTONEAREST
        if (monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info)) { return info.Work; }
        Rect area; SystemParametersInfoW(0x0030, 0, out area, 0); return area;
    }
}
'@
function Wait-For([scriptblock]$Condition, [int]$Seconds = 8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do { if (& $Condition) { return $true }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}
$logPath = Join-Path $env:LOCALAPPDATA 'DownloadsStack\app.log'
$logBefore = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }
# This check registers and withdraws the real sign-in entry, because that is the only place it exists.
# Whatever this machine had there is put back in the finally block, whichever way the run ends.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = 'DownloadsStack'
$autostartBefore = (Get-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue).$runValue
$primary = $null
$secondary = $null
try {
    $primary = Start-Process -FilePath $Executable -PassThru
    # Wait for the tray window rather than for a fixed delay: a first launch reads the whole image off a
    # cold disk, and a timer tuned to a warm one turns that into a spurious failure.
    $script:trayWindow = [IntPtr]::Zero
    $registered = Wait-For { $script:trayWindow = [DownloadsStackProbe]::FindWindowByTitle($primary.Id, 'Downloads Stack tray'); $script:trayWindow -ne [IntPtr]::Zero } 30
    $trayWindow = $script:trayWindow
    Start-Sleep -Seconds 1
    $primary.Refresh()
    if ($primary.HasExited) { throw "The primary instance exited with $($primary.ExitCode)." }
    if (-not $registered) { throw 'The process registered no tray window.' }
    # A tray application must not put anything on screen until it is asked to.
    $startsHidden = ([DownloadsStackProbe]::FindVisibleWindow($primary.Id) -eq [IntPtr]::Zero)
    # Explorer restarting is announced by a broadcast; a missed one leaves the user with no icon at all.
    $taskbarCreated = [DownloadsStackProbe]::RegisterWindowMessageW('TaskbarCreated')
    [DownloadsStackProbe]::PostMessageW($trayWindow, $taskbarCreated, [IntPtr]0, [IntPtr]0) | Out-Null
    Start-Sleep -Milliseconds 900
    # The shell reports a click on the icon as WM_LBUTTONUP inside the callback message. Delivering one here
    # checks the tray plumbing without a human at the taskbar, and checks it after a re-registration.
    [DownloadsStackProbe]::PostMessageW($trayWindow, 0x0401, [IntPtr]1, [IntPtr]0x0202) | Out-Null
    $trayClickOpens = Wait-For { [DownloadsStackProbe]::FindVisibleWindow($primary.Id) -ne [IntPtr]::Zero } 8
    $trayOpened = [DownloadsStackProbe]::FindVisibleWindow($primary.Id)
    if ($trayOpened -ne [IntPtr]::Zero) {
        # Closing by tray click cannot be checked from a script: this host holds the foreground, so the
        # flyout may already have closed itself on deactivation. Put it away over the window instead.
        [DownloadsStackProbe]::SendMessageTimeoutW($trayOpened, 0x0010, [IntPtr]0, [IntPtr]0, 2, 3000, [ref]([IntPtr]::Zero)) | Out-Null
    }
    Wait-For { [DownloadsStackProbe]::FindVisibleWindow($primary.Id) -eq [IntPtr]::Zero } 4 | Out-Null
    # A second launch has to reach the running instance over its pipe and open the list there.
    $secondary = Start-Process -FilePath $Executable -PassThru
    $secondExited = $secondary.WaitForExit(10000)
    $shown = Wait-For { [DownloadsStackProbe]::FindVisibleWindow($primary.Id) -ne [IntPtr]::Zero }
    $windowHandle = [DownloadsStackProbe]::FindVisibleWindow($primary.Id)
    if ($windowHandle -eq [IntPtr]::Zero) { throw 'The second launch did not open the list on the running instance.' }
    $extendedStyle = [DownloadsStackProbe]::GetWindowLongPtr($windowHandle, -20).ToInt64()
    $rect = [DownloadsStackProbe+Rect]::new()
    [DownloadsStackProbe]::GetWindowRect($windowHandle, [ref]$rect) | Out-Null
    $work = [DownloadsStackProbe]::WorkArea($windowHandle)
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
        trayIconSurvivesShellRestart = $trayClickOpens
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
    # How the installer stops the tray application before replacing or deleting its executable. It
    # returns only once the process is really gone, which is what keeps an upgrade off a reboot prompt.
    $quit = Start-Process -FilePath $Executable -ArgumentList '--quit' -Wait -PassThru
    $report.quitExitCode = $quit.ExitCode
    $report.exitsOnQuitRequest = ($primary.WaitForExit(8000) -and $quit.ExitCode -eq 0)
    if (-not $report.exitsOnQuitRequest) { throw 'The --quit request did not stop the running instance.' }
    # Starting with Windows, from the command line a scripted rollout of the portable build would use.
    Start-Process -FilePath $Executable -ArgumentList '--autostart-on' -Wait | Out-Null
    $report.autostartCommand = (Get-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue).$runValue
    $report.autostartRegistered = ($report.autostartCommand -eq ('"' + $Executable + '" --autostart'))
    Start-Process -FilePath $Executable -ArgumentList '--autostart-off' -Wait | Out-Null
    $report.autostartWithdrawn = $null -eq (Get-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue).$runValue
    foreach ($name in 'autostartRegistered', 'autostartWithdrawn') { if (-not $report[$name]) { throw "$name failed: $($report.autostartCommand)" } }
    Start-Sleep -Milliseconds 400
    $logAfter = if (Test-Path -LiteralPath $logPath) { (Get-Item -LiteralPath $logPath).Length } else { 0 }
    # Anything logged during a plain start, show and close is a defect worth reading before release.
    $report.loggedBytesDuringRun = $logAfter - $logBefore
    $report | ConvertTo-Json
} finally {
    foreach ($ownedProcess in @($secondary, $primary)) {
        if ($null -ne $ownedProcess -and -not $ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id -Force }
    }
    if ($null -ne $autostartBefore) { Set-ItemProperty -LiteralPath $runKey -Name $runValue -Value $autostartBefore }
    else { Remove-ItemProperty -LiteralPath $runKey -Name $runValue -ErrorAction SilentlyContinue }
}
