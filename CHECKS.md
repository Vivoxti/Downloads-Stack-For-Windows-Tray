# Checks on the fixes — 17.09.2026

- `dotnet test -c Release`: 33 passed, 0 failed, 0 skipped. They cover the existing file logic, the index, the settings, watching, system icons and CF_HDROP.
- The first Release build of the new version succeeded. The final publish is recorded at delivery.
- The application was started: the first run left the process running with no ordinary window available, exactly as tray mode intends.
- The interactive check through computer-use was stopped by the user with a physical Escape. The on-screen appearance, the icon clicks, hiding and reopening, the settings, scrolling and dragging in the new panel are NOT marked as checked.

Still to be checked by hand:

1. The icon is in the tray (possibly under the arrow) and there is no ordinary application button. A left click shows and hides the panel, a right click opens the menu.
2. Escape, a click outside and Alt+F4 hide the panel while the process and the icon stay. Exit removes the icon and ends the process.
3. The settings do not close because the parent lost focus; the folder picker works; buttons, rows and menus are readable.
4. Moving files into File Explorer, Ctrl/Shift and cancelling with Escape; the panel does not vanish mid-drag. The existing CF_HDROP tests do not replace this check.
5. DPI 100/150/200%, two monitors and an auto-hiding taskbar. No clipped names or buttons and no white default menus.
6. After an Explorer restart the icon comes back; running the application again does not create a second instance.

The earlier report on taskbar minimising and the old smoke-lifecycle.ps1 do not confirm that tray mode works.

## The background and Acrylic fix

The self-contained Release publish succeeded with no warnings and no errors. The panel and the settings use a transparent HWND background and Desktop Acrylic through DWM (attribute 38, value 3); the text is set explicitly light. On an API failure an opaque dark backdrop is used. The new build starts and stays in the tray. Visual confirmation of the blur has not been done yet: the capture tool did not get an open window it could reach. The file logic is untouched by this change; the full run of 33 tests belongs to the previous fix.

## Simplifying the panel

The Release self-contained publish succeeded with no warnings or errors, once the previous instance holding the DLL was stopped. The title, the footer and the buttons are gone. The panel takes only the newest rows that fit and unfolds them with the newest at the bottom. The ListBox template contains no ScrollViewer; the dotted focus rectangle is off. Visual acceptance of this version has not been done. Check that there are no partial rows, that the newest files come in order, that there is no scrolling, and that the settings are reachable from the tray.

## A fully transparent background

The self-contained Release publish succeeded with no warnings or errors. The main window uses per-pixel transparency, with no WindowChrome, no DWM Acrylic and no border; text and icons stay opaque. The rows carry alpha 1/255 so that their whole area is interactive, and highlighting happens only on hover or selection. Visual and drag acceptance of this version has not been done; check readability against a light background and dragging from an empty part of a row.

## Name backdrops

The real WPF template was rendered against a light background (artifacts/label-preview.png): a short name, an ordinary name, and a long one that gets shortened. The backdrop matches the length of the text; long text is bounded by the row width and keeps its .mp4. The Release self-contained publish succeeded. The render does not confirm dragging or the interaction with the tray.

## Drag and paths — 18.09.2026

A full dotnet test -c Release: 34 passed, 0 failed, 0 skipped. The new STA test creates a real WPF window with a test element, substitutes only the native drag operation, and checks Dragging/Hidden/Visible, the released mouse capture, the tray toggle being ignored during a drag, an immediate open and close after the native drag returns, and the tooltip delay parameters 2000/0. No user folders are scanned and no index is saved. The test does not confirm real OLE drops into File Explorer or the actual time it takes a tooltip to appear. The Release self-contained publish succeeded with no warnings or errors.

## The BAML error fix — 18.09.2026

The error was reproduced on the self-contained EXE: WPF was looking for Assets/app.ico on disk because the same resource was also declared as Content under a different name. The Content declaration was removed; the separate shortcut icon is copied by the Build/Publish targets, and app.ico remains purely an embedded WPF resource. Running the finished artifacts/win-x64/Downloads Stack.exe --check-ui loaded the Application resources, the main window, the settings and the tray icon successfully. The full exception log now carries the inner cause and the stack. scripts/publish.ps1 runs the resource check on the published EXE automatically.

## Animations — 18.09.2026

The STA test was extended: path delay 1000/0; start a close, reopen immediately, wait on the animation clock, and check that the old close does not hide the freshly reopened window; then let a normal close finish and reopen. The test uses an Application with resources only, controls the foreground to isolate itself from the desktop, and substitutes the native drag. The targeted test passed. How the hover animation looks in a live window has not been checked yet.

## The tray's reaction

A tray-active.ico resource was added (a blue variant of the white arrow with its alpha preserved), in 8 sizes. TrayService updates the icon and the text from the window state change event; Visible↔Dragging transitions do not flicker. The check on the published EXE loads both tray icons. The visual change of the icon in a real tray has not been checked yet.

A WPF regression test covers opening on one press and release, no selection on a left click, the open being suppressed after a drag, the right-click outline, and the 1.35 scale being held while the menu is open without the cursor being over the row.


The application icon was replaced from the source ChatGPT Image 18 Sep 2026, 00_31_17.png; the ICO contains sizes from 16 to 256 px. The active tray icon is coloured exactly #25BC96 with its transparency preserved; the RGB of every visible pixel was verified in all 8 sizes. The ordinary tray icon is unchanged.

System thumbnails: the tests compare the real colours of two different PNG/JPG/JPEG files of the same extension, cache reuse, frozen BitmapSource instances, a red frame from an H.264 MP4, and a corrupted MP4 falling back to the icon. The 2,275-byte test MP4 was created with ffmpeg; the application's runtime does not use ffmpeg. Visual acceptance on the user's screen has not been done.


The EXE's product name and description: Downloads Stack. The published apphost was renamed to Downloads Stack.exe without changing the internal assembly name; the WPF resource loading check on the published version passed. The existing shortcut was updated to the new EXE name.

The native context menu: an STA integration test obtains a real IContextMenu for a file with Cyrillic, spaces and an ampersand in its name, finds the system canonical verbs copy and properties, and checks that the HMENU is released. A WPF test checks that the enlargement and the outline are held and the tooltip disabled inside the menu's nested loop, and that cancelling leaves the list open. Visual submenus from third-party extensions have not been checked yet.

The system menu's theme: the build has no warnings; the published `--check-ui` check additionally calls NativeMenuTheme on a WPF HWND. The user's theme settings are only read. A visual comparison with File Explorer, and the user switching themes, were not done.

Smoothness: Windows reports DISPLAY1=240 Hz and DISPLAY2=120 Hz. An isolated WPF probe with 10 rows, a transparent window and a 3-second scale animation: the previous window at default gave 42.0 updates/s; with DesiredFrameRate=240 alone, 42.4; with BitmapCache on the surface as well, 113.8. That is the rate at which the property changes as seen through CompositionTarget.Rendering, not a count of frames physically shown or of DWM Presents; the mode and the load affect the result. The application gained a transition cache, a permanent text cache and an adaptive DesiredFrameRate.

A repeat measurement of the published version with the text backdrop and transition surface caches: the chosen monitor was detected as 240 Hz with a target rate of 240; 368 changes in 3.002 s = 122.6 WPF updates/s. The screen's physical FPS was not measured; a steady 240 FPS is not confirmed. All 40 regression tests and the published-resources check passed.

The tray menu: "Recent files" and the separator were removed; separate TrayContextMenu/TrayMenuItem styles were added. A WPF test checks that there are exactly two items, their order, the absence of a Separator and the #202020 colour.

## Stability, stutters and stuck states — 18.09.2026

`dotnet test -c Release`: 49 passed, 0 failed, 0 skipped. The self-contained Release publish and `--check-ui` succeeded.
`scripts/smoke-lifecycle.ps1` was rewritten for tray mode and is a valid acceptance check again: start with no window on screen,
a second launch opening the list of the running instance, closing the window hiding the panel into the tray without ending the process,
a single instance, the shortcut with its AppUserModel.ID, a clean shutdown. Over the whole run the application wrote not one byte into `app.log`.
The report is `artifacts/lifecycle-smoke.json`.

The causes of the stutters:

1. The scan opened a separate handle (`CreateFileW` + `GetFinalPathNameByHandleW`) per file in the folder on every pass,
   and a pass runs on any write inside a watched folder. A path inside an already resolved folder is now composed,
   and a handle is opened only for a reparse point.
2. The snapshot was published and the index rewritten after every pass and once a second while a file was still downloading —
   the panel rebuilt every row several times a second. The snapshot is now published only on a real change,
   and the index is written only when dates change, without indentation.
3. The one-second "settle" wait ran even on a folder of old files: every start and every open cost an extra second.
   The wait now kicks in only if something was written in the last 5 seconds.
4. `FileList.ItemsSource` was reassigned on every snapshot: hover, tooltips and name measurements were reset.
   Rows are recreated only when the visible list changes.
5. `Sources` was cleared and refilled on every snapshot — the folder list in the settings blinked and lost its selection.
   Only the items that changed are updated.

Locks and races:

6. The icon thread and `Dispose` both drained the queue, and the loser got an `ObjectDisposedException` on the UI thread;
   a crashed thread turned every subsequent icon request into an exception. The queue is no longer disposed from the thread,
   and when it stops, every pending wait completes with the fallback icon.
7. Publishing after the Dispatcher had stopped threw an `InvalidOperationException` from a background thread.
8. The tray menu was not counted in the open-menu counter, so a second right click created a second menu, and Settings
   opened a modal window on top of a menu that was still up. There is now one menu, it closes before the action, and Exit is deferred.
   Found and closed separately: the late `Closed` event of a replaced menu nulled the reference to its successor,
   after which the counter stayed non-zero and the panel would never have closed again.
9. Clicks on the icon keep arriving inside the modal loop — a second Settings window opened, and the panel opened underneath the modal window.
10. A second launch whose handshake failed showed the user an error window; now it only writes to the log.

Data and messages:

11. An unreadable or locked `settings.json` fell out of loading, and could be overwritten if the backup copy failed.
    The file is now left alone and a message is shown (a new `Error_SettingsRead` key in all 14 catalogs).
12. `Error`/`HasError` were not bound to any element: corrupted settings, a failed index write and a failed shortcut
    were shown nowhere. They are surfaced in the settings window, and the warning clears after a successful write.
13. The shortcut was rewritten on every start and reported an error every time on a read-only installation.
    It is written only if it does not already point at the current exe.
14. Icons were taken at 16 px and stretched to 24 px in a row and to 32 px on hover. The large system icon is taken instead.

New tests: no publications from a settled folder or during an unfinished download, the cost of repeated scans,
no index rewrite without changes, the settings file surviving a read failure, reuse of the panel's rows,
the tray menu being counted, the fallback icon being returned after `Dispose`, and thumbnail alpha and orientation.

Not checked: visual acceptance at 150/200% DPI and on a second monitor, dragging into File Explorer,
behaviour after an Explorer restart.

### Cleaning up after a removed feature

Unreachable panel handlers were removed (`ShowHeaderMenu`, `Refresh`, `Exit`, `OpenSelected`, `RevealSelected`,
`OpenSourceFolder`, `OpenSource`) — not one of them was bound in XAML — along with everything that existed only for them:
`ShellService.Reveal` and `ShellService.OpenFolder`, the `SHParseDisplayName` and `SHOpenFolderAndSelectItems` P/Invokes,
the unused `MonitorFromWindow`, the `DownloadItem.ParentPath` property, the `MainViewModel.ConfiguredSources` property,
the `IconButton` and `Separator` styles in `Theme.xaml`, and the `Error_FolderMissing` string in all 14 catalogs.
`MenuOpened`/`MenuClosed` were kept: they serve the tray menu and the native file menu.

Checked: the build has no warnings, `EnforceCodeStyleInBuild` finds no unused members or usings,
the catalog reconciliation script finds no orphaned localization strings, 49 tests pass,
and publish + `--check-ui` + lifecycle-smoke are green.

A consequence: there is no way to jump to a folder from the panel any more, in any form — a right click on a row opens
only the native Windows menu. If such a command is wanted, it will have to be added as a new feature with its own UI.

## Performance and resources — 19.09.2026

`dotnet test -c Release`: 60 passed, 0 failed, 0 skipped. The self-contained Release publish and `--check-ui`
succeeded; `scripts/publish.ps1` takes about a minute end to end. `scripts/smoke-lifecycle.ps1` is green with
`loggedBytesDuringRun = 0`. The report is `artifacts/lifecycle-smoke.json`.

Every measurement was made on this machine: the two builds were run one after another, interleaved, and the medians compared.
This is a desktop with browsers and other applications on it, so the absolute times drift;
interleaving removes the slow drift but does not make the numbers reproducible on other hardware.

### Re-reading a folder

The bench: `DownloadsService` on a temporary folder, 20 calls to `Refresh()` after the folder had settled;
`GC.GetTotalAllocatedBytes(precise: true)` and gen-0 collections are counted.

| Files in the folder | Allocated per pass, before | after |
|---|---|---|
| 100 | 158 KB | 2 KB |
| 1,000 | 1,553 KB | 2 KB |
| 10,000 | 15,869 KB | 2 KB |

The remaining 2 KB are the `async` state machines; they do not depend on the size of the folder. Gen-0 collections
over 20 passes on 10,000 files: 41 before, 0 after. The first scan of 10,000 files: 103 ms and 20.2 MB before, 78–94 ms
and 7.2 MB after. Steady managed-heap memory at 10,000 files: 12.2 MB before, 5.7 MB after; the bench's working
set was 62.9 MB before and 46.2 MB after. Publications over the 20 passes are still zero.

The regression is covered by the `RescanningASettledFolderCostsNothingPerFile` test: 2,000 files, 10 passes,
a 1 MB threshold. The previous code allocated about 32 MB on the same bench. Parallel execution of test classes
is off: this test and the earlier timing checks would otherwise have been measuring the machine's load rather than the code.

### Startup and idle

| Metric | Before | After |
|---|---|---|
| Panel on screen, `--show`, median of 12 interleaved runs | 687 ms | 628 ms |
| The same, minimum | 639 ms | 595 ms |
| Process CPU by the time it is shown + 1.2 s | 1,328 ms | 1,156 ms |
| Process CPU by the time it is idle in the tray (10 s after launch) | 1,188 ms | 953 ms |
| Working set in the tray, panel closed | 140.6 MB | 135.5 MB |
| Private memory in the tray, panel closed | 86.3 MB | 87.7 MB |
| Modules loaded in the tray | 145 | 139 |
| Publish size | 173 MB, 475 files | 142 MB, 405 files |

Private memory while idle barely moved: WinForms occupied mostly shared code pages, and the gain from removing it
shows up in the working set, in the module count and in the work done at startup, not in private bytes.

The startup breakdown was taken with temporary tracing (since removed): before the changes, of the 763 ms before
the panel appeared, 191 ms went to the `MainWindow` constructor, 218 ms to `InitializeAsync` (creating the HWND and
the initial composition setup) and 158 ms to the first show and paint.

`PublishReadyToRunComposite` was tested and rejected. A first run from a folder the system had not read yet:
4,072–4,272 ms against 1,274–1,424 ms for ordinary ReadyToRun and 1,439–1,518 ms for the previous build. On a warm
file, composite was worth about 50 ms. For an application that starts at sign-in that is a bad trade.

### The tray icon

`smoke-lifecycle.ps1` now finds the hidden tray window by its title, sends it a broadcast
`TaskbarCreated`, then a real `WM_LBUTTONUP` in the callback message, and waits for the panel to appear —
the `trayIconSurvivesShellRestart` field. That exercises the `.ico` parsing, the registration, the re-registration
and the delivery of the click. Closing the panel by clicking the icon is not checked by the script: the host holds
the foreground, and the panel may close on deactivation before the click arrives; instead the panel is closed with `WM_CLOSE`.

The `TrayIconTests` parse the real `tray.ico`, `tray-active.ico` and `app.ico`, create HICONs at
16/24/32/40/48 px and use `GetIconInfo`/`GetObject` to check that the bitmap has the requested size and 32 bits
per pixel — that is, that the image of the right size was chosen rather than a stretched one. A missing resource
returns 0 rather than a silently empty icon.

The check itself was fixed separately: `anchoredAboveTaskbar` compared the window against the work area of the **primary**
monitor, whereas the panel opens on the monitor under the cursor. On a second monitor the check failed
on the previous build too. It now takes the work area of the window's own monitor.

### Not checked

- How the tray icon looks and how it changes while the panel is open — nobody has looked at it.
- A real Explorer restart: only the `TaskbarCreated` message was sent, Explorer itself was not restarted.
- Visual acceptance at 150/200% DPI and on a second monitor, dragging into File Explorer.
- Bounding icon prefetch to sixteen rows was verified by reading the code; there is no counter of requests to the shell.
- Changing a symlink's target inside a watched folder without changing its name — the path exists in the code, there is no test.

### Memory: where the 100 MB comes from, and the rendering setting — 19.09.2026

The question was put as: "100 MB is too much for such a simple little program." It was measured and broken down.

First, what each number means. A `Working Set` of 135 MB includes shared DLL pages;
`Private Bytes` of 87 MB is everything committed, including what has never been paged in; what Task Manager shows is
`Private Working Set` — 62 MB in the tray and **106 MB after the panel is opened once**, and it does not
come back down: a measurement after 45 seconds of idling gave 106.1 MB.

Two baselines, built and measured with the same ruler (self-contained, ReadyToRun, private working set):

| | privateWS |
|---|---|
| .NET without WPF: a hidden Win32 window and a message loop | 3.7 MB |
| A WPF `Application` with no window | 6.1 MB |
| WPF with one window | 54.2 MB |
| WPF with one window and `RenderMode.SoftwareOnly` | 10.7 MB |
| Downloads Stack in the tray (before) | 61.4 MB |

From which: all the code, the data, the index, the folder watching, the icon cache, the XAML and the localization amount to
61.4 − 54.2 ≈ **7 MB**. The rest appears the moment WPF's first window is created and belongs to
the Direct3D device and the graphics driver (`amdxn64.dll` — 38.9 MB of mapped code on its own).

The same binary, differing only in rendering mode:

| | idle | after 10 opens | CPU over 10 cycles |
|---|---|---|---|
| Direct3D | 62 MB | 106 MB | 3.1–3.3 s |
| `SoftwareOnly` | 17 MB | 32–38 MB | 4.4–5.7 s |

The published build with the default setting: **15.8 MB in the tray, 29.6 MB after ten opens,
29.3 MB another 45 seconds later**, with a working set of 117 MB.

Switching `ProcessRenderMode` on a running application was tested separately and gives no memory back:
93.3 MB before the switch, 93.0 MB straight after, 88.4 MB twenty seconds later — that is the ordinary shrinking of a
working set, not the device being released. So the setting is read once at startup, before the tray window is
created, and the caption under the checkbox says it takes effect after a restart.

The appearance was checked by rendering the settings window and both states of the checkbox to PNG through `RenderTargetBitmap`.
The new `Settings_Hardware` and `Settings_HardwareHint` keys were added to all 14 catalogs; the localization
test requires the key sets to match exactly. There are now 62 tests. The user's settings file was unchanged after
the test run — verified by hashing it before and after.

**Not checked:** how noticeable software rendering is to the eye at 240 Hz. Only CPU was measured;
there is no counter of frames actually shown, and the previous iteration invested in smoothness specifically —
if the difference turns out to be visible, the checkbox in the settings can be turned back on.

## Closing the tray menu

The menu from a right click on the icon stayed on screen until an item was picked: a click past it, Esc and switching to
another application did not dismiss it. The cause is not WPF — when the icon is clicked the shell activates
nobody, and a background process's popup receives neither the mouse capture nor the keyboard, so every
event outside the menu goes to whoever owns the foreground.

What was done: before the menu opens, the hidden tray window takes the foreground (KB135788); after it closes,
`WM_NULL` is posted to that same window and the foreground is returned to the previous window (or to the flyout, if it is
open) — but only if nobody else has taken it. While the menu is open, a 120 ms timer checks the foreground:
if it went somewhere other than us, the menu closes. On top of that the menu closes on a left click on the icon, on
any other icon message (middle click, double click, the X buttons) and on a DPI or resolution change.

The foreground alone turned out not to be enough: the menu still stood on screen. The system is not obliged to honour
`SetForegroundWindow` on the invisible tray window, and without the foreground WPF does not get the mouse
capture either — that is, nothing at all reaches the process about a press outside the menu. So the input state
is now asked of the system directly: while the menu is open, a 50 ms timer checks `GetAsyncKeyState`
for the five mouse buttons and Escape and compares `WindowFromPoint` against the popup's window. A press anywhere outside the menu,
Escape and the foreground leaving all close the menu; a press on the menu itself does not. The window under the cursor was taken
instead of the popup's rectangle: the menu is drawn in a layered window with room for its shadow, and a press in that
margin is a press past the menu.

Checked: the build and all 62 tests. Two checks were added to `FlyoutInteractionTests`: the foreground moving
to a foreign window closes the menu and zeroes the menu counter, and a mouse button press over a foreign window
(`PointerButtonDown`/`WindowUnderPointer`) closes it too. The second check also confirms that
the popup has a real window by that point — otherwise the "outside the menu" check would not have fired and the test would have failed.

For manual checking, `scripts/dev-build.ps1` was added: one self-contained `Downloads Stack.exe`
in the project root (134 MB, in `.gitignore`), a couple of seconds incrementally. The script refuses to run
while that build is running: the application is a single instance, so the old copy has to be closed through Exit.
`--check-ui` passes on the built file. Startup time must not be measured on it — ReadyToRun is off,
and a single file also unpacks its native libraries on the first run; `publish.ps1` exists for that.
A single-file build without `--self-contained` crashes at startup here (0xC000041D) — hence the self-contained one.

**Not checked by hand:** a live right click on the tray icon. The published build in `artifacts/`
is older than both fixes.

## The name backdrop's opacity — 19.09.2026

All 65 tests pass (46 s). New ones: `backdropOpacity` survives a write and a read; zero is read as
a real zero rather than as a missing field; a file without the field yields 85 and does not end up in `.corrupt-*`;
a value of 140 is clamped to 100 with the folder list intact; and the percentage maps to alpha as 0 → 0,
85 → 217 (exactly the old `#D9`), 100 → 255, 400 → 255.

Checking the slider in the settings dialog required showing the window: in a window that has never been shown,
the bindings stay `Unattached` and `Slider.Value` stays zero regardless of the model.
So the test calls `Show()`/`Hide()` around the check — only that way is it visible that the slider really
takes the saved percentage and that building the dialog writes nothing.

`Downloads Stack.exe` in the root was built through `scripts/dev-build.ps1`.

The plate's right padding was raised from 8 to 14 DIP; that affects neither the row width nor where the name is cut, and the 65 tests
pass after the change (48 s), with the exe in the root rebuilt.

The background of the panel's whole surface was changed from `Transparent` to `#01000000`, so that the mouse does not fall through
the gaps and the panel's margin. The test checks the brush specifically: WPF treats `Transparent` as hit-testable and cannot tell the two
cases apart with its own hit-test — it is Windows that makes the pixel see-through, by the alpha of a layered window. 65 tests pass
(45 s), with the exe in the root rebuilt.

**Not checked by hand:** how the slider looks and how the panel repaints while it is dragged on a live screen; how the
deferred write behaves if the settings window is closed within the first 400 ms after the slider moved; how the new right
padding looks on the longest name, the one shortened with an ellipsis; a live check that the cursor over a gap
no longer highlights the window behind.

## Choosing the sort order — 19.09.2026

All 69 tests pass (50 s). New ones: every field really does set the order and the checkbox flips the
list as a whole (three files, each leading by exactly one field, so reading the wrong field cannot pass);
`sortBy`/`sortReversed` survive a write and a read, a file without them means the previous order, and an unknown
word in `sortBy` falls back to it without sending the settings to a backup copy; changing the order rebuilds
the list from what the source already knows — a file created an instant beforehand does not make that snapshot
and arrives on its own, already in the chosen order. The dialog check gained a case that the dropdown offers
every field, shows the saved value, and that building the dialog writes nothing.

The order is applied through `DownloadsService.ApplyOrder`: it touches no disk and restarts no watching,
so changing the item in the list does not cost a single directory enumeration. Cutting a source off at a hundred
stays correct meanwhile: the comparison is a total order with tie-breaks on name and full path, so a source's
hundred-and-first file cannot reach the combined hundred under any field.

The access time is read from the same directory entry as the name and the size, and is deliberately kept out of a file's
"stamp": otherwise every read of a file would count as an unfinished write. `NotifyFilters` is not subscribed to access
time, so ordering by it catches up on the next folder scan rather than at the moment the file is opened.

WPF's `ComboBox` has no dark styling — the template of the dropdown and of its rows is written out in full in `Theme.xaml`.
`ComboBoxItem` derives from `ListBoxItem`, but implicit styles are bound to the exact type, so the list's rows
need a style of their own.

The closed box is drawn not by the row container but by `SelectionBoxItemTemplate`, and `DisplayMemberPath` does not reach it
inside a custom template: the selected item was shown as `SortOption { Field = …, Text = … }`.
An explicit `ItemTemplate` was given instead of the path — then the same markup reaches both the rows and the closed box.
The dialog test pins that down: `SelectionBoxItemTemplate` equals `ItemTemplate` and is not null.

The settings window was rebuilt into three labelled groups separated by lines: folders, sorting,
appearance. The "Add folder", "+ Downloads", "Remove" and "Retry" buttons moved out of the bottom row to under
the folder list they act on; only "Done" stays at the bottom. The reverse-order checkbox sits
on the same line as the dropdown. The folder list is capped at 240 DIP instead of 300, and the window's maximum height
was raised from 560 to 680.

The window's content sits in a `ScrollViewer`, with "Done" in a separate row below it. The window still grows
with its content and stops at the maximum height, but beyond that the settings scroll rather than pushing
the button off the bottom edge; previously a long folder list or a long error message would have done just that.

The styling was checked by rendering: the window was built in a separate process with the real `Theme.xaml` and drawn
through `RenderTargetBitmap` into a PNG — the closed box, the popup part with all six items and the selected one highlighted,
the three groups with their lines, the rearranged buttons, and the scrolling behaviour with a list of twelve folders.

`Downloads Stack.exe` in the root was built through `scripts/dev-build.ps1`.

**Not checked by hand:** all of the same on a live screen, where a render says nothing: the dropdown opening upwards
at the bottom edge of the screen, the popup's appearance animation, DPI 150/200% and the highlighting of the row under the cursor.
Whether the longest item in the list and the checkbox's caption get clipped in languages with long words.

## Startup and delivery — 19.09.2026

All 78 tests pass (49 s). The nine new ones are about startup: turning it on writes a command with quotes around
the current exe's path; turning it off removes the value and turning it off again is not an error; an entry disabled in
Task Manager's Startup apps reads as "blocked" rather than as "on", and the user's answer there stays
in place after the entry itself is removed; an entry pointing at a file that is gone is rewritten to point at itself,
while one pointing at an existing second copy is left alone; and the path is extracted from the command both with quotes
and without, and out of junk such as an unclosed quote. The tests work in a branch of their own,
`HKCU\Software\DownloadsStack.Tests\<guid>`, which they delete afterwards: a run has no business touching the
real startup configuration of the machine it runs on.

The settings dialog check gained cases that the "Start with Windows" checkbox shows what the registry
says, that the line about Task Manager is visible only for a blocked entry, and that building
the dialog changes nothing in the registry — the state is read before the model is created and compared afterwards.

`scripts/smoke-lifecycle.ps1` gained three checks against the real exe: `--quit` stops
a running instance (and returns 0 only once the process has really gone), `--autostart-on` writes
exactly `"<path>" --autostart` into `Run`, and `--autostart-off` removes that line. The entry's previous value on
the machine is restored by the script in a `finally`, however the run ends. A full run against the build from
the root: 21 checks, all passing, `loggedBytesDuringRun` = 0.

The styling of the "Startup" group was checked by rendering the settings window to PNG in both states, ordinary and
blocked: the group caption, the checkbox, the grey hint with the same 30 DIP indent as the hardware acceleration
hint, and the red line underneath it. The blocked state was set directly on the elements for the render:
writing into the machine's real `Run` key for the sake of a screenshot would be dishonest. The first version of the harness did
exactly that — it clicked the checkbox, the dialog's handler ran for real and registered startup for a
scratch copy of the exe; the entry was removed and the harness rewritten to detach the handler before showing the state.

### The installer: what was measured

The package is built with WiX 6 (a local tool of the repository, `dotnet tool restore`) and was checked by installing
and uninstalling on this machine: 401 files and 141 MB in `%LOCALAPPDATA%\Programs\Downloads Stack` without UAC,
a Start menu shortcut with `AppUserModel.ID` = `Vivoderin.DownloadsStack`, the application launching at the end,
and uninstalling stopping it, taking away the folder, the Start menu shortcut and the shortcut the application writes next to
itself, without asking for a reboot. An upgrade from 1.0.0 to 1.0.1 was checked separately: the running instance
is stopped before the files are replaced, the new version runs afterwards, and startup, if the user had turned it on,
stays on.

Turning startup on from the installer could not be made to work, and that is worth writing down so that it is not attempted again:

1. A custom action running `exe --autostart-on` after `InstallFiles` fails with 1721 — everything
   scheduled inside the installation script runs at the moment the script is composed, when the files are not on
   disk yet. After `InstallFinalize` the action does run and returns 0, the program writes the value
   and immediately reads it back as written (verified with a diagnostic file: the same user,
   the same SID, the same `LOCALAPPDATA`), but from outside the value is in no hive of `HKEY_USERS`.
2. The same with a `<RegistryValue>` in a component of the package. The log shows `RegAddValue` with the right name and
   the expanded path under `RegOpenKey(Root=-2147483647)`, that is HKCU — and after the install the value is not there.
   Polling the registry every 50 ms during the installation did not see it for a moment. Neighbouring values of the same
   component in the same key (`ZZAutostartProbe`, `ZZ Spaced Probe`) do get written.
3. Writing the same name by hand from the same application outside an installation sticks and does not disappear, including
   while the application is running. Deleting the value from a custom action on uninstall also "succeeds" and also
   does not happen.
4. The behaviour reproduces on a minimal four-line package, and the first installation of a completely
   new name goes fine: the value appears and uninstalling removes it. After one
   install/uninstall cycle that same name stops being written — from any package, including one with a different
   `UpgradeCode` and a different component GUID. The cause could not be found; there are no entries for that name in
   `Installer\UserData\<SID>\Components`.

Hence the decision: the `Run` key is owned by the application and the installer does not touch it. That also settles the question of
what a package repair or upgrade does to the user's choice — nothing.

A side finding, documented in the package markup as well: `<RemoveRegistryValue>` in WiX 4+ has no
`Action` attribute and lands in the `RemoveRegistry` table, which Windows Installer processes when a component is
**installed**, not when it is removed. That element cannot be used to clear a value on uninstall only. And another:
`VersionNT` on Windows 11 is 603, so the condition `VersionNT >= 1000` in `<Launch>`
lets the installation through nowhere — the first install attempt failed on it with 1603.

**Not checked:** how startup behaves after a real sign-in (nobody rebooted);
installing and uninstalling on a clean machine and under another user; blocking the entry through
Task Manager's Startup apps for real — the state was checked by a test and by a render, but not by a click in Task
Manager itself; an upgrade while the running instance is not the installed one but a portable copy.

## The number of files in the list — 19.09.2026

All 81 tests pass (50 s). New ones: `maxVisibleItems` survives a write and a read, a file without the field
yields 10 and does not end up in `.corrupt-*`, and a value of 0 is clamped to 1 with the folder list intact; in the settings
dialog the slider runs from 1 to 20, snapping to whole values, and shows the saved number without
writing anything while the window is built. A separate test opens a real panel with five files: at 10 all five
are visible at a height of 270 DIP, at 2 there are two rows and 120 DIP with the newest file still at the bottom, and at 20
there are five again. That checks not the arithmetic but the wiring: `PreviewMaxVisibleItems` → `LayoutChanged` →
recomputing the window.

A WPF limitation surfaced along the way, which is why the second windowed test failed at first: there can be only one
`Application` per process, however many UI threads there are, and `Shutdown()` does not remove it from `Application.Current`.
The tests now take the shared `Application` through `EnsureApplication()` and shut down only their own
dispatcher at the end, so the order they run in decides nothing.

The settings window's maximum height was raised from 680 to 740 DIP. The content height was measured by rendering with the
cap disabled: Japanese 697, English 711, Russian and German 726 DIP each — at 680 the window would have
scrolled always. The styling was checked by rendering to PNG in four languages: the new row comes first
in the "Appearance" group, the slider and the number line up with the opacity slider, and there is no scrollbar.

`Downloads Stack.exe` in the root was built through `scripts/dev-build.ps1`. The running instance was stopped
through `--quit` (the file is locked otherwise) and started again from the new build.

**Not checked:** the behaviour on a monitor where the chosen number of rows does not fit — on this machine
twenty rows do fit into the work area.

## The portable build as one executable — 19.09.2026

The portable package used to be a folder of 401 files. It is now a single executable, and the archive holds
nothing else. Everything below was measured on this machine with the builds run one after another,
interleaved, six times each; the medians are within 1% of one another and the first run of each set, which
reads the file cold, is left in the table as the outlier it is.

The stopwatch is around `--check-ui` on the published executable: it loads the Application resources, the
main window, the settings and the tray icons, which is the part of startup a bundle can make slower.

| | to download | unpacked | `--check-ui`, median |
|---|---|---|---|
| Folder of 401 files (before) | 60.9 MB | 401 files, 140.6 MB | 1024 ms |
| One executable, in the archive | 61.5 MB | 1 file, 149.5 MB | 1020 ms |
| One executable, bare | 149.5 MB | 1 file | 1020 ms |
| One executable, `EnableCompressionInSingleFile` | 67.0 MB | 1 file | 2028 ms |

So the bundle costs nothing to load: 1020 ms against 1024, which is noise. Compression was dropped and the
reason is the last row: it halves the download but doubles the load, on every launch and not just the first,
because the assemblies are decompressed into memory each time rather than extracted once. This application
starts at sign-in, so a second of CPU per launch is the wrong thing to trade a download for.

The archive rather than the bare executable was chosen for the release: the same 61 MB to download as
before, and unpacking yields one file instead of a folder. `ReadyToRun` stays on, which `dev-build.ps1`
turns off for build speed — that is the whole difference between the file in the project root and the
released one.

`ShortcutService` now takes the shortcut's icon from the executable instead of a `DownloadsStack.App.ico`
beside it. Without that, the shortcut the application writes next to itself would have had no icon in a
portable folder holding one file. The apphost carries the same image through `<ApplicationIcon>`, and the
installed copy is unaffected: the installer keeps using the `.ico` for its own Start menu shortcut, which is
compiled into the package.

**Not checked:** how the single file behaves against an antivirus other than the one on this machine —
an unsigned executable that unpacks native libraries into `%TEMP%` on first run is exactly the shape
heuristics dislike, and nothing here says how a given scanner will treat it.

## An installer for one user or for all — 19.09.2026

> Superseded the same day by "An ordinary per-machine installer" below, after the wizard was clicked
> through for the first time. The scope choice was never reachable; the package is per-machine only.

The package now declares `Scope="perUserOrMachine"` and carries WiX's `WixUI_Advanced` wizard. Both branches
were installed and uninstalled on this machine with a stub payload, which builds in seconds and exercises
exactly the parts under test — the folder each branch resolves, the registry root, where the Start menu
shortcut lands, and whether anything is left behind.

| | folder | Start menu | marker | uninstall |
|---|---|---|---|---|
| Default, not elevated (`/qn`) | `%LOCALAPPDATA%\Programs\Downloads Stack` | the user's | `HKCU` | nothing left |
| `ALLUSERS=1`, elevated (`/qn`) | `C:\Program Files\Downloads Stack` | all users | `HKLM` | nothing left |

Every install and uninstall returned 0, and the per-machine install left nothing under `%LOCALAPPDATA%`.

Three conditions were wrong before these two passed, and every one of them was found by installing rather
than by reading the markup:

1. An `APPLICATIONFOLDER` override conditioned on `WixAppFolder = "WixPerUserFolder"`. `WixAppFolder` is
   mixed case, therefore a private property, therefore not settable from a command line — it says "per
   user" throughout a silent `ALLUSERS=1` install. The package registered itself per machine and put its
   files in the installing user's profile.
2. The same override conditioned on `MSIINSTALLPERUSER AND ALLUSERS = "2"`, which failed the other way:
   in the server process the condition was false, and a per-user install landed in the dialog set's own
   `[LocalAppDataFolder]Apps` rather than in `Programs`.
3. Overriding `APPLICATIONFOLDER` at all. The dialog set's own actions are guarded by
   `APPLICATIONFOLDER=""`, so they step aside for a value set earlier — but the scope dialog's Next button
   assigns `APPLICATIONFOLDER=[WixPerMachineFolder]` as a control event, which walks over anything the
   sequence set. Only redefining `WixPerUserFolder` and `WixPerMachineFolder`, right after the dialog set
   computes its defaults, covers the sequenced path and the interactive one at once.

Both defaults needed replacing. `WixPerMachineFolder` is built from `ProgramFilesFolder`, which in a 64-bit
package still resolves to `C:\Program Files (x86)`: the first successful per-machine install put a 64-bit
application there. `WixPerUserFolder` is `[LocalAppDataFolder]Apps`, while the versions before this one
installed into `Programs`, where Windows puts per-user applications and where an upgrade should stay.

The component key paths moved from `HKCU` to `HKMU`, which resolves to `HKLM` for a per-machine install and
`HKCU` for a per-user one. `ShortcutService` now treats a refused write as nothing to do rather than as an
error: under Program Files a standard user cannot write the shortcut it puts beside the executable, and a
per-machine installation carries a Start menu shortcut with the same `AppUserModel.ID` anyway.

The real package was then installed for all users and the application started from
`C:\Program Files\Downloads Stack` at the ordinary user's integrity level, which is the case the read-only
folder exists in. It ran, it wrote no shortcut beside itself, and it put one line in `app.log`:
`Shortcut (read-only folder): System.UnauthorizedAccessException: Access is denied. (0x80070005)`. Nothing
was reported to the user, which is the point of that catch. The first attempt at this check proved nothing:
it drove msiexec from an already elevated shell, so the installer's own launch action started the
application elevated and it wrote the shortcut quite happily. Getting back down to the user's own level
took handing the path to `explorer.exe`.

**Not checked:** the wizard itself. Everything above was driven silently; nobody has clicked through the
welcome page, the licence, the two radio buttons or the Browse dialog, and no one has seen how the pages
look. The per-machine branch has also only been installed by the machine's own administrator, not by a
standard user answering a UAC prompt with someone else's credentials.


## An ordinary per-machine installer — 19.09.2026

Clicking through the wizard found three faults that no amount of silent installing had: the pages ran in a
strange order with the folder hidden behind an "Advanced" button on the licence page, choosing "for all
users" still offered an AppData path, and browsing to a folder installed into that folder rather than into
a `Downloads Stack` inside it.

The middle one is the interesting one. `WixUI_Advanced`'s Next button on the scope dialog carries seven
control events, and this is the first of them:

    Ordering 1:  WixAppFolder = "WixPerUserFolder"   when:  1 AND NOT Privileged

A package started by a double click is not elevated, so the answer is put back to "only for me" before any
of the events that read it: `ALLUSERS` is cleared, `APPLICATIONFOLDER` comes from the per-user branch, and
the wizard moves on to the features page. The per-machine option is only honoured when msiexec is already
running elevated. No condition of ours could have fixed that, which is why the previous section's work is
superseded rather than corrected.

The package is now `Scope="perMachine"` with `WixUI_InstallDir`, and the browsed folder is the parent of
the application's own. Verified by installing and uninstalling the built package:

| | `APPLICATIONFOLDER` resolved to | uninstall |
|---|---|---|
| Default | `C:\Program Files\Downloads Stack\` | nothing left |
| `INSTALLPARENTFOLDER=C:\DownloadsStackParentTest` | `C:\DownloadsStackParentTest\Downloads Stack\` | nothing left |

Every install and uninstall returned 0. The default landed in the 64-bit Program Files, not the (x86) one,
and the chosen-folder case left no loose files in the parent — the count of files directly inside it was
zero. A folder chosen in the wizard reaches the package as `INSTALLPARENTFOLDER`, which is what passing it
on the command line does, so the second row is the browse case.

The page order was read out of the package rather than watched: `WelcomeDlg` → `LicenseAgreementDlg` (on
`LicenseAccepted = "1"`) → `InstallDirDlg` → `VerifyReadyDlg`.

**Not checked:** the pages themselves, again. Nobody has clicked through this wizard either; what is
verified is where its properties end up, not how it looks. Upgrading from the per-user installs that
versions 1.0.0 to 1.1.0 left behind is also unchecked, and is not expected to work: Windows Installer scopes
upgrade detection, so a per-machine package will not find a per-user product. Those have to be removed
through Installed apps first.
