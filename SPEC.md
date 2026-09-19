# Downloads Stack — specification for the first version

> Changed on the user's direct instruction: an icon IN THE TRAY next to the clock is required. Section 3 replaces the original model of a pinned taskbar window. Older mentions of pinning, minimising, Alt+Tab, a light interface and the old sizes below are to be treated as cancelled; the current behaviour and checks are in README.md and CHECKS.md.
Date: 17.09.2026. Goal: a simple Windows 11 application that opens, from the system tray, a combined list of the newest files in folders the user has chosen; the Downloads folder is connected by default. This document is meant for the implementing model: build the application in the stages below without widening the scope.

## 1. Feasibility and boundaries

The references show a macOS Dock folder displayed as a stack, opening in a fan. What is needed here is an ordinary vertical list with no fan, no previews, no animations and no decorative interface. [Apple's description](https://support.apple.com/en-lamr/guide/mac-help/mchl231f08fb/mac).

This is achievable with an ordinary desktop application. The taskbar button belongs to the application's own compact window. Do not inject code into Explorer, do not install drivers or taskbar modifiers. Do not substitute a tray icon next to the clock for the requested button. Windows ties taskbar buttons to windows; they are not arbitrary buttons with a click handler of their own. [The Windows taskbar model](https://learn.microsoft.com/en-us/windows/win32/shell/taskbar).

Decisions taken for the MVP:

- The sources are a configurable list of folders, initially the system Downloads folder. Adding and removing folders in the settings is part of the MVP. Take only the immediate files from each one; do not walk into subfolders.
- This is a combined list of what is in the chosen folders, not a log of internet downloads: files copied there by hand are visible too; files outside the chosen folders are not. Do not read browser databases. Do not use the Zone.Identifier mark as a mandatory filter: it is not a complete download log.
- One file per drag operation. Opening is a double click or Enter; a single click selects the row, as in File Explorer.
- The window appears above the bottom edge of the work area of the chosen monitor. Exact alignment over the application's own taskbar button is not required: do not search Explorer's internal UI to find its coordinates.
- The application has an ordinary Alt+Tab entry and a system taskbar preview. That is an acceptable difference from the Dock.
- Pinning is done by the user through "Pin to taskbar". Without pinning, the button only exists while the application is running.

## 2. Technology

| Component | Decision and reason |
|---|---|
| Language / UI | C# + WPF: a small native Windows interface, access to the Shell and to OLE |
| Runtime | .NET 10 LTS, `net10.0-windows`, `UseWPF=true` |
| Windows API | P/Invoke and COM interop, collected in a layer of their own |
| Files | System.IO, FileSystemWatcher, JSON for a small index |
| Delivery | A self-contained publish for win-x64 into an ordinary folder; no mandatory runtime install |

.NET 10 is under LTS support until November 2028. [Microsoft's policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

Do not use Electron, a browser UI, a server, SQLite, a DI framework or a UI library for the sake of one list. Plain MVVM without a third-party package is fine. No administrator rights. ARM64, an installer, auto-update and startup are later versions.

## 3. Tray and panel behaviour — the user's clarification

A permanent icon sits in the system tray next to the clock, not among the buttons of open applications. Use NotifyIcon; ShowInTaskbar=false, ShutdownMode=OnExplicitShutdown. The first run creates the icon without showing the panel. Windows may place the icon under the hidden-icons arrow; describe that in the README, do not change Windows settings by force.

A left click on the icon shows and hides the compact panel; Escape, Alt+F4 and loss of activation hide it through Hide() without ending the application. Account for the deactivation that arrives before the click on the tray, so that a second click does not open the panel again. Context menus, the settings, folder selection and a drag temporarily suppress the automatic hide. Exit in the menu ends the process and releases the icon. Running the application again shows the existing instance.

A window without the ordinary title bar, a dark theme, its own styles for buttons, rows and menus, and the system DWM rounding. The settings use the same style. The panel appears above the taskbar on the monitor under the cursor, constrained by the work area; do not centre it like an ordinary application. The size is roughly 392 DIP wide and up to 560 DIP tall; rows are 50 DIP, and files with the same name carrying a path go up to 66 DIP. Keep the native drag and the source settings.

## 4. The minimal interface

- Width 380 DIP, height by content, at most 480 DIP and no more than the work area minus the margins. A row is about 34 DIP.
- A short "Recent files" title at the top. Below it a scrollable virtualised list of at most the 100 newest files across all the sources, without grouping by folder.
- Each row: a 20–24 DIP system icon and the name with its extension. When a long name is shortened, keep the extension; the tooltip shows the full path.
- An "Open folder" command at the bottom: with a single source it opens that one, with several it shows a short source-picking menu, and with none it is disabled. The title menu: "Settings", "Refresh", "Exit". The file context menu: "Open", "Show in File Explorer". A full shell context menu is not required.
- Identical names from different folders stay as separate rows; only for such collisions show a second, dimmed line with the parent path (increase the row height by content). The tooltip always contains the full path.
- ↑/↓ select, Enter opens, Escape collapses. Visible focus, accessible element names.
- An empty list: "There are no files in the chosen folders yet." If there are no sources — "Add a folder in the settings", with a command that opens the settings. If some folders are unavailable, keep showing the available files and a compact "Folders unavailable: N" message linking to the settings. Do not present complete unavailability as an empty list.
- A plain background with readable contrast and row highlighting. No thumbnails, cards, search, grouping, theme settings or counters.

## 5. Sources, settings, sorting and refresh

### Folder settings

A small "Settings" dialog: the list of connected folders with their full path and status, plus "Add folder…", "Remove from list" and "Done" buttons. Adding uses the system single-folder picker. Save changes immediately and apply them without a restart. Removing a source only stops watching it and drops its rows from the application; it does not delete or move any files on disk. Downloads can be removed too, leaving the source list empty. If Downloads was removed, offer an "Add Downloads" command to restore the system source.

Store `%LOCALAPPDATA%\DownloadsStack\settings.json`: `schemaVersion`, `sources[]`, each with a stable `id`, a `kind` (`downloads` or `directory`) and, for `directory`, an absolute `path`. Add Downloads automatically only when the initial configuration is created; a saved empty list is to be treated as deliberate. The write is atomic; report a save failure rather than claiming the change was saved. Keep a corrupted settings file as a separate backup copy and restore the default configuration, telling the user.

For the system source, obtain the path through `SHGetKnownFolderPath(FOLDERID_Downloads)` rather than by concatenating `%USERPROFILE%\Downloads`: the folder may have been moved. Resolve it again on startup and when the sources are refreshed; free the returned memory. [Known folders](https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid).

Normalise paths to absolute ones, allowing for drive roots and a trailing separator; compare with OrdinalIgnoreCase. Adding the same resolved path again does not create a second source but selects the existing one. For accessible directories, resolve junctions and symlinks to the final path when checking for duplicates; merge files by their normalised, resolved full path. Treat hard links with different paths as separate items. Allow a parent folder and a nested one at the same time: there is no recursion.

Keep a source that has become unavailable in the settings with an error status and a "Retry" action. Drop its stale items from the active list but keep its date index. Resume watching on the next open or refresh; the failure of one folder must not block the rest. Handle removable and network paths in the background with limited concurrency; do not pile up repeated hung operations on one source. Do not promise instant cancellation of hung file I/O.

### Files and dates

Enumerate files in the background. Exclude directories, Hidden/System entries and the endings `.crdownload`, `.part`, `.partial` and `.tmp`, case-insensitively. This is a heuristic, not a universal test for a finished download. For a new or changed file, wait for the size and LastWriteTime to stay the same across two checks one second apart. Do not require opening the file exclusively and do not exclude zero size. Treat a browser renaming a temporary file into its final name as a finished file appearing; then check stability as well. A slowly written file can pass the heuristic — describe that limitation in the README.

Sorting should approximate when a file appeared, not when it was last edited:

1. For files that already exist on the first run and on the initial scan of an added folder, take `CreationTimeUtc`; that is an approximation, not a proven download time. Do not lift a whole added folder to the top with today's date.
2. For newly discovered finished files, record `FirstSeenUtc` as the current time; for an ordinary rename of a finished file, keep the previous value.
3. On startup, date newly discovered files that have no index entry by `CreationTimeUtc`: when they appeared while the application was stopped is not known exactly.
4. Merge the sources, remove duplicate paths, sort by the effective date descending; on a tie, by name and then by full path. After merging, take the first 100. Changing the contents of an existing file does not lift it to the top.

The index: `%LOCALAPPDATA%\DownloadsStack\index.json`, holding the source id, the path and the effective date; path comparison is OrdinalIgnoreCase. Update it atomically through a temporary file. After a specific source has been scanned successfully, delete its missing entries; do not touch entries of unavailable or not yet scanned sources. When a source is removed from the settings, clear its index entries. Recreate a corrupted index. Do not store file contents or download URLs.

A separate `FileSystemWatcher` for every unique available source: Created/Changed/Renamed/Deleted/Error, without recursion. Debounce the events by 300–500 ms per source; do not count on them being unique or complete. On a buffer overflow, rescan the affected source; on startup and on every open, rescan all of them. No continuous full polling. Deferred stability checks apply only to candidates. When a source is added, attach the watcher before the initial scan and then reconcile the events collected meanwhile; when one is removed, dispose of the watcher and ignore its outstanding results through a configuration generation number. [FileSystemWatcher's limitations](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher).

Update the UI only through the Dispatcher. Do not start competing scans uncontrolled: use cancellation and a generation number; a stale result must not replace a newer one. Preserve the selection and the scroll position across updates. Defer changes during a drag.

## 6. Opening, icons, dragging

Opening: the full path through `ProcessStartInfo` with `UseShellExecute=true`, without `cmd.exe` and without assembling a shell command by hand. Windows picks the application by association. Check for existence before the call, but also catch a failure of the call itself. Do not bypass the system's confirmation prompts for running downloaded executables. Minimise the list after a successful launch.

"Show in File Explorer": `SHOpenFolderAndSelectItems`; "Open folder": a shell open of the folder. Names with spaces, Cyrillic, quotes and shell characters must be handled as paths, not as commands.

Icons: `SHGetFileInfoW` with the small system icon. Fetch them off the UI thread on a dedicated STA thread, with a bounded queue and a cache; after copying the HICON into a WPF image, always call `DestroyIcon`. For ordinary types a cache keyed by extension is fine; for exe and lnk, key it by path and modification time. On failure, use the generic icon. [SHGetFileInfoW](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow).

A drag starts when the left button is held past the system thresholds `MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance`. The press itself does not open the file. Before the drag, capture the full path and check that it exists. After a drag, do not invoke the open handler.

For the final MVP, use the Shell data object of the real file:

1. On the UI STA thread, obtain an IShellItem through `SHCreateItemFromParsingName`.
2. `IShellItem.BindToHandler(BHID_DataObject, IID_IDataObject)` yields the native COM IDataObject.
3. Call `SHDoDragDrop` with that object, the window's HWND, `pdsrc=null`, allowing Copy | Move. Check the HRESULT; Cancel is a normal outcome.
4. Hand over the existing file; do not create a temporary copy of it and do not replace the object with the path as text.
5. Copying, moving, choosing the effect and the conflict dialogs are the Shell's and the receiver's job. Do not delete the source by hand when Move comes back: that can end in deleting it twice. Re-read the folder afterwards; release the COM resources the service owns.

This route was chosen for an interaction close to File Explorer's. A plain WPF `DataFormats.FileDrop` + Copy is acceptable only for an early prototype and does not meet the requirements for moving. Do not mix `System.Windows.IDataObject` with the native COM IDataObject. [Shell data object](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitem-bindtohandler), [SHDoDragDrop](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shdodragdrop), [Shell transfer semantics](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard).

Whether a file is accepted depends on the target application: promise compatibility with applications that accept ordinary file drops, not with every program. Test a drop into File Explorer and into a browser's upload area; run both sides without elevation. Incoming drags into our list, virtual files and right-button drags are out of scope for the MVP.

## 7. Project layout

One WPF project and a small test project for the pure logic:

```text
src/DownloadsStack/
  App.xaml(.cs)                 startup, single instance, shutdown
  MainWindow.xaml(.cs)          the list, focus, gestures, window states
  MainViewModel.cs              items, selection, commands, errors
  SettingsWindow.xaml(.cs)      folders, add/remove, statuses
  Models/FolderSource.cs        id, source kind, path
  Models/DownloadItem.cs        source, path, name, date, icon
  Services/SettingsStore.cs     source configuration, atomic write
  Services/DownloadsService.cs  sources, scanning, watchers, merging
  Services/IndexStore.cs        the JSON index and its atomic write
  Services/ShellService.cs      open/select, icons, drag
  Interop/                      only the Win32/COM declarations that are needed
tests/DownloadsStack.Tests/
README.md
```

Do not perform large I/O on the UI thread. Dispose of watchers, timers, the mutex, the pipe, HICONs and COM objects on shutdown. Write errors into a bounded local log, without file contents. The target: after loading, the list opens in roughly 200 ms from the cache, and there is no constant CPU load while idle. That is a testable target, not a promise of equal speed on any disk.

## 8. Implementation order and acceptance

1. **The risky prototype:** a window with three real files, the taskbar, minimise and restore, a Shell drag. Check pinning, a second launch, focus loss, a move and a cancel. Do no styling work until that succeeds.
2. **Data:** source configuration, the Known Folder, scanning, the date index, watchers, filtering, stability and merging of files.
3. **UI:** the virtualised list, system icons, folder settings, commands, the keyboard, errors and DPI.
4. **Delivery:** a Release publish, a README covering how to run it, manual pinning, the limitations and the results of the checks. Do not mark checks that were not carried out as passed.

Mandatory checks on real Windows 11:

- The pinned button launches or restores the list; after Escape and after a click outside, the button stays. No second instance is created. There is no empty large window and no second button.
- Downloading in Chromium and Firefox: the temporary name is not visible, and the final file appears after it settles. Creating, renaming and deleting in File Explorer is reflected in the list.
- The order survives a restart; editing an old file does not make it new. A corrupted index recovers without the loss of any user file.
- Adding and removing a folder takes effect without a restart and survives one; removing a source does not change any file on disk. Removing every source does not bring Downloads back automatically. A newly added folder does not get today's date across the board.
- Two folders with identically named files: both files are visible with distinguishable paths, and the right one is opened and dragged. Adding a path again — including with different casing, a trailing separator or an accessible junction — does not duplicate the list. The limit of 100 is shared.
- One unavailable source does not hide the available ones; recovery brings the files back with their previous dates. Removing a folder mid-scan does not bring its rows back through a late result. The settings and the folder picker do not collapse the main window and do not create a second taskbar button.
- Double click and Enter open the file with the right application; a file that has disappeared and a missing association do not bring down the process.
- A drop into File Explorer: copying and moving, Ctrl/Shift, across different drives where possible; Escape cancels. Check for the source and the target file, not just the cursor. A name conflict is resolved by the system dialog.
- A drop into a browser's file-upload area transfers the file. Losing focus during a drag does not break the operation.
- An empty, unavailable or moved folder, long names and Cyrillic, 10,000 files, 100/150/200% DPI, two monitors, a taskbar set to auto-hide.

Automated tests are needed for filtering, the combined order and limit, path de-duplication, date persistence, index recovery and the difference between "no settings" and "a saved empty source list". The window, the Shell and dragging are to be checked manually as integration; pure-logic tests do not prove that the interaction with Windows works.

An example of delivery:

```powershell
dotnet test -c Release
dotnet publish src/DownloadsStack/DownloadsStack.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o artifacts/win-x64
```

What the implementer delivers: the sources, a runnable build folder, the README and an honest list of the checks that passed and failed. Do not add later features until this acceptance is met.


## Current clarification on styling

At the user's request of 17.09.2026, the panel and the settings must use a translucent dark backdrop with the system Desktop Acrylic, not a white or fully opaque background. WPF's Window.Background and CompositionTarget.BackgroundColor are transparent, WindowChrome.GlassFrameThickness=-1, DWM_SYSTEMBACKDROP_TYPE=DWMSBT_TRANSIENTWINDOW. The text is explicitly light; do not change the window's own transparency through Window.Opacity, so that text and icons stay contrasty. When the API is unavailable, fall back to an opaque dark backdrop.

## A minimal panel — the user's latest clarification

The open panel holds nothing but file icons and names with their extension. Remove the title, the decorative icon, the gear, the ellipsis, the open-folder button and the separators. The settings and Exit are reachable through a right click on the tray icon. Pick the newest files that fit into the panel's height completely, then show them from older at the top to newest at the bottom. Do not display partial rows. No ScrollViewer, no scrollbar, no scrolling with the wheel or the keyboard. A row is 50 DIP including padding, and the panel's height is the sum of the rows plus 22 DIP; at most 560 DIP and at most the monitor's work area. The tooltip holds the full path; the second folder lines are gone. The empty state is one short line of text without buttons. This clarification cancels the earlier requirements for a title, a footer, extra lines and a scrollable list. Cancelled along with them are the title menu and the application's own "Open folder" and "Show in File Explorer" commands: the only row menu is the native Windows shell context menu, so `SHOpenFolderAndSelectItems` and the shell open of a folder are no longer used in the application. There is no way to jump to the source folder from the panel.

## A fully transparent list — latest clarification

The main panel has no background, no blur, no dimming, no border and no window shadow. Use WPF AllowsTransparency=True with WindowStyle=None and Background=Transparent, without WindowChrome and without applying a DWM backdrop. Keep the text and the icons opaque. A barely visible shadow on the text alone improves readability; a row is highlighted only on hover or selection. For hit-testing the whole row, an all but invisible backdrop at alpha 1/255 is acceptable, so that a file can be opened or dragged from anywhere in its row. The settings stay a separate dialog with Acrylic. The order (newest at the bottom), the absence of scrolling and the number of fully fitting rows are unchanged.

## Text readability — current clarification

Place a local dark backdrop with rounded corners and soft blurred edges behind every file name. The backdrop adapts to the width of the visible text and takes no part in measuring the row. Blur the backdrop only, never the text. Keep the overall background transparent, the file order and the absence of scrolling. Shorten text while keeping the extension.

## Fixing the drag and the path delay — 18.09.2026

Release the WPF Mouse.Capture before SHDoDragDrop and in the finally block. Ignore the tray toggle during a drag. On completion or cancellation, reset the drag state, resume updates and hide the panel without blocking the next tray click. For the deactivation check, take GetForegroundWindow into account, not just IsActive. The path tooltip: InitialShowDelay=2000, BetweenShowDelay=0 (disable the accelerated hand-off between tooltips); disable and close tooltips during a drag and while hidden. Re-enable them on restore.

## Animations and the path delay — current clarification 18.09.2026

The path tooltip delay is 1000 ms, BetweenShowDelay=0. Remove the hover/selected row fill and keep the accessible keyboard focus outline. Hovering anywhere on the row scales the icon 1→1.35 with BackEase/EaseOut (180 ms) and lifts it by 2 DIP; leaving returns it over 120 ms. Animate the RenderTransform, not the size or the layout. The transparent list appears with opacity 0→1, scale .96→1, translateY 12→0, over 150–170 ms. Closing: opacity→0, scale→.98, Y→8 over 95 ms, then Hide. A separate Hiding state and a transition number prevent a stale Hide after a reopen. Disable hit-testing and tooltips while closing. Before a drag, stop the appearance animation and return the transform to a stable state. The panel follows the system's ClientAreaAnimation setting.

## The tray icon's state

Use the white original icon while the list is closed and a blue version of the same icon while it is open. The Visible and Dragging states count as open; Hiding, Hidden and Exiting count as closed. Change NotifyIcon.Icon from the logical-state change event, without polling. Switch the tray tooltip to the action: "Open the list" / "Close the list". Dispose of both icons and unsubscribe from the event on shutdown.

### Opening with one click and selecting with a right click
Open the file when the left button is released over the same row where the press began. A drag that has started suppresses the open; there is no double-click handler. A left click does not outline the row. A right click opens the context menu and marks its row with an outline through FileRowState.IsContextTarget. The icon animation is active on IsMouseOver OR IsContextTarget: moving the cursor into the menu does not shrink the icon. Closing the menu clears the mark; if the cursor is still over the row, the enlargement stays. Do not rebuild the row containers while the menu is open; apply the current list once it closes.


### System thumbnails instead of media icons
This requirement replaces the earlier ban on thumbnails. For .png/.jpg/.jpeg/.mp4, obtain an IShellItemImageFactory through SHCreateItemFromParsingName and request GetImage with SIIGBF_THUMBNAILONLY at 96x96 without cropping the aspect ratio. Use IconService's existing background STA thread, not the UI thread. Convert the HBITMAP into a frozen BitmapSource, and release the HBITMAP through DeleteObject and the COM object once it has been copied. When no thumbnail is available, fall back to SHGetFileInfo. Cache media by full canonical path and LastWriteTimeUtc, not by extension; cap the cache at 256. Fetch thumbnails only for the rows that fit into the open panel. Display at 24x24 DIP with Stretch=Uniform and HighQuality; the enlargement on hover and on right click is unchanged. For MP4, use the Windows system handler, with no ffmpeg in the application.


### The Windows Shell context menu
Remove the custom file menu. On the right button's release, obtain IContextMenu from IShellItem.BindToHandler(BHID_SFUIObject), create an HMENU through CreatePopupMenu and fill it with QueryContextMenu over the command range 1–32767. Shift turns on CMF_EXTENDEDVERBS. Show it with TrackPopupMenuEx(TPM_RETURNCMD|TPM_RIGHTBUTTON), the list's HWND and the click's screen coordinates. Zero means cancellation; pass the command to InvokeCommand as the offset commandId-1 through CMINVOKECOMMANDINFOEX with Unicode and ptInvoke, taking Shift and Ctrl into account. While the menu is up, forward WM_INITMENUPOPUP/WM_DRAWITEM/WM_MEASUREITEM/WM_MENUCHAR to IContextMenu3 or IContextMenu2 through a temporary HwndSourceHook. Release the HMENU, the COM objects and the hook in a finally block. For the duration of the nested native loop, suppress hiding and row rebuilding, disable tooltips, and hold IsContextTarget and the enlarged icon. On cancellation, clear the outline and check for deactivation; after a command, refresh the data and hide the panel. A Shell error is surfaced by the normal handler, not silently replaced by a custom menu. This is the classic Windows 11 menu; Explorer's new compact menu is not exposed through this API.

### The native menu's theme
Before creating the system menu, read AppsUseLightTheme from HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize, without writing Windows settings. In NativeMenuTheme, optionally load uxtheme.dll from SystemDirectory and resolve ordinal 135 (SetPreferredAppMode), 133 (AllowDarkModeForWindow), 104 (RefreshImmersiveColorPolicyState) and 136 (FlushMenuThemes). Following the app theme, set ForceDark or ForceLight for our process only; under SystemParameters.HighContrast, use Default. Allow the dark theme for the owner HWND and flush the menu cache. These ordinal APIs are undocumented; limit them to Windows 10 1903+, check that the exports exist, and fall back to the ordinary system menu when they do not. Add Common-Controls v6 to the manifest. The published EXE's `--check-ui` check runs the setup against a real HWND and reports whether it is available.

### Smoothness on high refresh-rate monitors
There was no hard 60 FPS cap in the previous code: DesiredFrameRate was never set (null). Before animating, read the chosen monitor's current mode through GetMonitorInfoW(MONITORINFOEXW) and EnumDisplaySettingsW(ENUM_CURRENT_SETTINGS) and set Timeline.SetDesiredFrameRate on every DoubleAnimation; fall back to 60 when the data is unavailable. Turn on BitmapCache on FlyoutSurface for the duration of an open or close and remove it once the transition completes or is cancelled; guard the completion with transitionId. Cache the static backdrop and text Grid. Move the icon animations out of the XAML Storyboard into MouseEnter/MouseLeave handlers with a shared Motion factory, so that the rate follows a change of monitor. Clone the frozen TransformGroup before animating it. A right click holds the enlargement until the menu closes; the system's "turn off animations" setting is honoured for the icons too. Do not run a permanent frame timer while idle. Requested FPS is not to be taken as the screen's measured FPS.

## Performance and resources — 19.09.2026

This iteration is about what running the application costs. Visible behaviour, styling and the feature set did not change.

### Re-reading a folder

Walking a watched folder is the application's only continuous load: it runs on any write inside that folder and repeats every few hundred milliseconds while a download is in progress. Each pass used to copy the entire folder: the `Stable` and `Aliases` dictionaries, the candidate list, a `DownloadItem` per file, index keys of the form `source\0path`. On a folder of ten thousand files that is 15.9 MB of garbage per pass.

Now a source's state is a single `Files` dictionary keyed by file name, whose values survive across passes. The directory is enumerated through `FileSystemEnumerable`: `FileSystemEntry` hands over the name, the attributes, the size and the write time straight from the directory entry, with no `FileInfo` and no name string. The name is looked up in the dictionary as a `ReadOnlySpan<char>` through `GetAlternateLookup`. A file whose size and write time match is stamped with the pass number and costs not one allocation. The path string is created once in a file's lifetime and reused; for an ordinary file the canonical path is that very same string, and the `Aliases` dictionary holds reparse points only. Deletions are detected by comparing the count of entries seen with the dictionary's size; equality means there is no reason to walk the dictionary at all.

The date index is stored nested, by source and then by path: the composite key used to be built for every file on every pass. A file's date is remembered in its entry and asked of the index once — when it first appears. `Prune` runs only if something really did disappear.

A source publishes not all of its files but its newest hundred — exactly as many as will survive `FileRules.Merge`. That is safe: canonical paths are unique inside a source, so a source's hundred-and-first file cannot make it into the combined hundred. If a pass changed nothing, the list is neither rebuilt nor published.

Confirming that a file has finished being written no longer opens a handle per file on a large folder: past 64 changed entries it is cheaper to enumerate the directory a second time.

### A tray icon without WinForms

`UseWindowsForms` is gone. For the sake of one `NotifyIcon`, `System.Windows.Forms.dll` (13.1 MB) and `System.Windows.Forms.Primitives.dll` (3.4 MB) were mapped into the process along with their satellites, all of it loaded at startup. The icon is now registered by calling `Shell_NotifyIcon` on a hidden window of our own (`HwndSource`, WS_POPUP without WS_VISIBLE, WS_EX_TOOLWINDOW). The window must be top-level rather than message-only: an Explorer restart is announced by the broadcast `TaskbarCreated` message, which a message-only window never receives. NIM_DELETE runs before re-registering, otherwise NIM_ADD on a live icon returns an error. If the shell is not listening yet — the application started before the taskbar — registration is retried five times with a one-second pause.

The icon is assembled from the packed `.ico` by hand: ICONDIR is parsed, the image matching `GetSystemMetricsForDpi(SM_CXSMICON)` is picked, and its PNG is handed to `CreateIconFromResourceEx`. The callback message is `WM_APP+1`; `WM_LBUTTONUP` opens the panel and `WM_RBUTTONUP` opens the menu. This supersedes section 3's earlier instruction to use `NotifyIcon`; the icon's behaviour is unchanged.

### Build and startup

`PublishReadyToRun` is on. `PublishReadyToRunComposite` is not, and that is a measured decision: the single 157 MB image opened the panel in 4.1 s on a machine that had not read it yet, against 1.3 s for ordinary ReadyToRun, in exchange for roughly 50 ms on a warm file. A tray application starts at sign-in, off a cold disk — precisely the case composite makes worse.

The settings and the index are read from disk in parallel with each other, and start being read in the model's constructor while WPF is still building the window and creating the HWND. The refresh rate and the monitor scale are cached until `WM_DPICHANGED` or `WM_DISPLAYCHANGE`: `EnumDisplaySettingsW` is a call into the display driver, and positioning happens on every open. Icons are requested only for the rows that physically fit on screen: the panel is capped at 560 DIP with a row height of 50, so sixteen is enough, and asking the shell about the other eighty-four is work the user will never see. `FileNameText` reuses the `Typeface`: fitting a name's width costs about eight measurements, and each of them used to build a new typeface.

### Rendering: a setting instead of the WPF default — 19.09.2026

Measurements showed that the overwhelming majority of the application's memory is Direct3D, not its own data. An empty WPF application with no window takes 6.1 MB of private working set; the same one with a single window takes 54.2 MB; the same one with a single window and `RenderMode.SoftwareOnly` takes 10.7 MB. The device is created when the first window appears and is never released: for the application itself, 62 MB in the tray turned into 106 MB after the panel was opened once, and stayed there.

Hence the `hardwareRendering` setting in `settings.json`, off by default. A file written before the field existed reads as "off" rather than as a schema error. The value is applied in `App.OnStartup` before the tray window is created — the first thing to get a render target. Changing the mode on the fly is pointless: it was tested, and switching `ProcessRenderMode` on a running application gave back not one megabyte (93.3 → 93.0 MB), so the checkbox in the settings honestly says it takes effect after a restart.

The price of software rendering is CPU time during the open and close animations, and only there: while idle, WPF does not draw at all. Ten open/close cycles cost about 3.2 s of CPU with the graphics card and about 4.3–5.7 s without it.

### The name backdrop: opacity in the settings — 19.09.2026

The dark plate behind a file name was hard-coded as `#D9101114` — 85% opacity. At the user's request of 19.09.2026 its alpha was moved into the settings as a slider from 0 to 100%. Zero leaves the name over the bare desktop (the text shadow and the blurred edges remain), a hundred makes the plate opaque.

The value is stored as `backdropOpacity` in `settings.json`, 85 by default — exactly what the XAML had, so the look does not change unless someone changes it. A file written before the field existed reads as 85 rather than as a schema error. A number out of range is not treated as corruption: it is clamped, because otherwise editing one number by hand would cost the user their whole list of folders.

The list's rows take one shared frozen brush from the model (`BackdropBrush`) rather than each having its own colour: moving the slider repaints the open panel immediately, recreating nothing. The write to disk is deferred by 400 ms after the last change and also happens when the settings window closes — otherwise one gesture of the mouse would mean a hundred rewrites of `settings.json`. The setting applies on the fly; no restart is needed.

At the same time, and also at the user's request, the plate's right edge was extended: the padding became asymmetric — 8 DIP on the left (exactly the text's own left margin, as the plate is drawn from the start of the column) and 14 on the right. The last character, or the ellipsis of a shortened name, used to press against the edge.

### The number of files in the list: a slider in the settings — 19.09.2026

How many rows the panel showed was decided by a single constant: the height was capped at 560 DIP, which with a 50 DIP row is exactly ten files. At the user's request of 19.09.2026 the number was moved into the settings as a slider from 1 to 20 (whole values, `IsSnapToTickEnabled`), 10 by default — those same ten, so the look does not change unless someone changes it.

The value is stored as `maxVisibleItems` in `settings.json`. A file written before the field existed reads as 10 rather than as a schema error; a number out of range is clamped, just like `backdropOpacity` — editing one number by hand must not cost the user their list of folders. Zero is clamped to one: an empty panel leaves nowhere to get back to the settings from.

The 560 DIP constant in the positioning code was replaced by a desired height of `20 + N × 50`, constrained by the monitor's work area. The monitor keeps the last word: on a short screen there will be fewer rows than requested, and there are still no partial rows. The empty state is measured against the work area rather than against the chosen number, otherwise at N=1 the short line of text would not fit.

The model exposes the number as `MaxVisibleItems` and announces a change to it through a separate `LayoutChanged` event: moving the slider rebuilds the open panel even though not one file changed. The write to disk is deferred by the same timer as the backdrop opacity — 400 ms after the last movement, and on closing the settings window. Icon prefetching is bounded by the same number: asking the shell about rows that will not be on screen is work the user will never see.

The settings window's maximum height was raised from 680 to 740 DIP: with the new row the content takes from 697 (Japanese) to 726 DIP (Russian, German), and the window would have scrolled every time it opened. Verified by rendering to PNG in four languages.

### The mouse no longer falls through the panel — 19.09.2026

The panel is a layered window (`AllowsTransparency=True`), and Windows hit-tests the mouse by the pixel's alpha: where there is no alpha at all, the cursor and the click go to the window behind. The invisible 1/255-alpha backdrop was only on the rows themselves, so the gaps between rows (2 DIP above and below) and the panel's 10 DIP margin stayed see-through: running the cursor down the list, the user was also hovering the application in the background. Now the same `#01000000` background covers the panel's whole surface. It looks no different (0.4% black), but a click on a gap no longer goes outside and no longer closes the panel as a click past it; it is still closed by Escape, by the tray icon and by a click outside the window.

### Starting at sign-in — 19.09.2026

At the user's request of 19.09.2026, a "Start with Windows" setting and all the machinery behind it were added. The per-user `Run` key in `HKCU` was chosen over a scheduled task or a file in the Startup folder: no administrator rights are needed, one and the same entry works identically for an unpacked portable copy and for an installed one, and the user sees it in Task Manager's Startup apps next to everything else — that is, they can turn it off where they go looking for such things. The value is named `DownloadsStack`, and its data is the path to the exe in quotes plus `--autostart`: a path with a space and no quotes would be read by Windows as two arguments and would launch nothing.

The state lives only in the registry. There is deliberately no copy in `settings.json`: the same switch exists in Task Manager, and the settings file would start lying the moment the user used it. The settings window re-reads the registry every time it opens; reading one value costs microseconds and is done synchronously, because what has to be shown is the truth, not what used to be true.

The state has three values, not two. Windows can disable the entry without deleting it: the command stays and the user's answer is kept in `Explorer\StartupApproved\Run`, where the low bit of the first byte is the switch. Such an entry is shown by the application as a ticked checkbox plus a separate line saying that Windows disabled startup in Task Manager and that it can only be turned back on there. Otherwise the user would get a checkbox that does nothing. The application does not overwrite somebody else's answer in `StartupApproved`, neither when turning startup on nor when turning it off.

The entry is repaired at startup: if the file at the recorded path is gone, it is rewritten to point at the current exe. That covers a portable folder that was moved and an installer that replaced the previous copy. If the file at the old path is still there, leave it alone: one run of a second copy must not take startup away from the one that was registered. A failed repair is only written to the log: the settings window will show the real state of affairs anyway.

The windowless command line: `--autostart-on`, `--autostart-off` and `--quit`. The last one asks a running instance to close and returns only once its mutex is gone, that is, once the process has really ended — the only reliable sign that the exe has been released. The single-instance protocol was extended with a third command (`Exit`) on top of the earlier "show the list".

### Delivery: portable build and installer — 19.09.2026

`scripts/package.ps1` builds the release: a zip with one executable inside, and an MSI. They come from two
publishes, because a single-file bundle and the folder the installer carries are different layouts. The installer is per-user, into `%LOCALAPPDATA%\Programs\Downloads Stack`, without an administrator and without UAC. That is how it should be: the application belongs to one user, so does its startup entry, and it writes a shortcut next to its own exe, which a folder under Program Files would not allow.

The installer does not touch the `Run` key — neither on install nor on uninstall — and that is the result of measurement, not of saved effort. A registry change made inside a Windows Installer transaction does not survive that transaction: neither the package's own `RegistryValue`, nor a write by a program launched from a custom action which immediately re-reads it and sees it written. The numbers and the course of the check are in `CHECKS.md`. So the entry has one owner, the application, and the consequence is written down honestly in the README: uninstalling does not remove startup if it had been turned on.

The package takes responsibility for what it can do reliably: the files, a Start menu shortcut with the same `AppUserModel.ID`, removal of the shortcut the application writes next to itself, and stopping a running instance through `--quit` before the files are replaced — otherwise an upgrade would end in a request to reboot. The action that launches the exe comes after `InstallFinalize`: everything scheduled inside the installation script runs at the moment the script is composed, when the files are not on disk yet (error 1721).

### Installing for one user or for all — 19.09.2026

At the user's request of 19.09.2026 the installer offers what most Windows programs offer: an install for
everybody under Program Files, a folder that can be chosen, and a wizard rather than a silent install. The
package therefore declares `Scope="perUserOrMachine"` and uses WiX's `WixUI_Advanced` dialog set.

Per-user stays the default, and with it the property defaults Windows Installer reads as per-user
(`ALLUSERS=2`, `MSIINSTALLPERUSER=1`) and the folder the previous versions used. The dialog set would have
put a per-user install under `[LocalAppDataFolder]Apps`; a `SetProperty` scheduled after
`WixSetPerUserFolder` puts it back under `Programs`, where Windows installs per-user applications and where
the installed copies already are, so an upgrade stays in place instead of moving.

Only the per-machine branch offers a folder to browse to. That is the dialog set's rule, not a decision
made here: for a per-user install it fixes the path and hides the browse button.

Two things follow from Program Files being read-only to a standard user. The key paths of the components
move from `HKCU` to `HKMU`, which resolves to `HKLM` for a per-machine install and to `HKCU` for a per-user
one. And `ShortcutService`, which writes a shortcut beside the executable on every start, treats a refusal
to write as nothing to do rather than as an error: a per-machine installation carries a Start menu shortcut
with the same `AppUserModel.ID`, which is the whole purpose of the one beside the executable, and only a
portable copy has nobody else to provide it.

The startup entry stays per-user in both, because it is a per-user decision: installing for all users puts
the program on the machine, it does not start it for everybody.
