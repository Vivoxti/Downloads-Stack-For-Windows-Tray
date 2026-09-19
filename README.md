# Downloads Stack

A Windows 11 application: an icon in the system tray, next to the clock, opens a compact list of the newest files from the folders you choose.

[![Release](https://img.shields.io/github/v/release/Vivoxti/Downloads-Stack-For-Windows-Tray?label=release&color=25BC96)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Vivoxti/Downloads-Stack-For-Windows-Tray/total?label=downloads)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/releases)
[![Build](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/actions/workflows/ci.yml/badge.svg)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Windows 11 · .NET 10 · WPF · a self-contained build, with nothing to install beforehand.

The panel is fully transparent: all you see are the system file icons and the file names. One click opens a file, dragging hands over a real Shell data object, a right click brings up the classic File Explorer context menu. There can be several source folders; Downloads is connected by default.

## Install

Ready-made builds live on the [releases page](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/releases/latest). Both packages carry the same application — the only choice is how it gets onto the machine.

| Package | File | What it does |
| --- | --- | --- |
| Installer | `DownloadsStack-<version>-win-x64.msi` | An ordinary wizard — welcome, licence, where to put it, install — for all users, into `C:\Program Files\Downloads Stack`. One UAC prompt. The folder can be changed, and whichever folder is picked gets a `Downloads Stack` subfolder inside it. Adds a Start menu shortcut, launches the application when it finishes, and is removed through Installed apps. |
| Portable | `DownloadsStack-<version>-portable-win-x64.zip` | One executable inside. Unpack it anywhere and run `Downloads Stack.exe`. Nothing appears anywhere in the system until you turn on startup yourself. |

The application is not signed with a certificate, so SmartScreen will warn you on the first run: More info → Run anyway.

If you want it without administrator rights, take the portable build: one executable, and nothing written outside the folder it sits in.

Starting with Windows works the same way in both packages — a checkbox in the settings; see the section below.

## Running it

Run `Downloads Stack.exe` — from the installed folder, from the unpacked archive, or from `artifacts/win-x64` after a local build. No window appears on the first run: the application lives in the tray. If Windows put the icon under the hidden-icons arrow, drag it out next to the clock. Pinning it to the main taskbar is not required.

- Left click on the icon shows and hides the list.
- Escape, Alt+F4 and a click outside hide the list and leave the application running.
- Right click on the icon opens the list, the folder settings and Exit.
- Running the EXE again opens the list of the instance that is already running.
- `--show` opens the panel immediately on the first run (useful for checking).
- `--quit`, `--autostart-on`, `--autostart-off` are windowless commands: stop the running instance, turn startup on and off. See "Starting with Windows".
- Exit in the menu ends the process completely and removes the icon.

Before you start a new build, close the previous instance through Exit. Otherwise running it again just activates the version that is already there.

## The list and its settings

A fully transparent panel: no blur, no dimming, no border, no window shadow. All that is visible are the system file icons and the names with their extensions. A row is highlighted on hover and on selection. The text carries a slight shadow so it stays readable against the desktop. The full path is available in the row's tooltip. The settings window keeps its own Acrylic styling.

The settings have a "Hardware graphics acceleration" checkbox, off by default. Without it the application takes about 16 MB in the tray and 30 MB with the list open, instead of 62 and 106 MB: nearly all of that difference is the Direct3D device that WPF creates along with the first window and never gives back. The price is CPU time during the open and close animations, and only there. The switch takes effect after a restart.

The system Downloads folder is connected by default. The folder settings are one right click on the tray icon away. Removing a source does not delete any files. The newest files are picked from all the selected folders, without walking into subfolders. The panel shows only the rows that fit completely, and does not scroll. The first file of the chosen order sits at the bottom, closest to the tray icon. There is no title, no buttons and no bottom bar.

The "Files in the list" slider sets how many of the newest files the panel shows: from 1 to 20, 10 by default — exactly as many as used to fit. The panel rebuilds itself immediately, right under the slider. The monitor gets the last word: if the rows do not fit into the work area, there will be fewer of them than you asked for. The value is stored in `settings.json` as `maxVisibleItems`.

The settings also have a "Sort" dropdown and a "Reverse order" checkbox. You can sort by date added, name, type (extension), date modified, date created and date accessed. "Date added" is the application's original order — when the file showed up in the folder; the other dates are read from the file itself, which is why "date created" can differ from it for a file that was moved. Dates run newest first, name and type run alphabetically, and the checkbox flips the whole list. The choice takes effect at once, without a restart and without re-reading the folders, and is stored in `settings.json` as `sortBy` and `sortReversed`. Ordering by date accessed catches up on the next folder scan: Windows sends no notification when a file is read, and the application does not watch for it.

A single left click, or Enter, opens the file. A right click outlines the row and opens the context menu; the icon stays enlarged while the menu is open. Dragging hands over a real Shell data object; whether that copies or moves is up to the receiving application. The panel does not hide on focus loss during a drag. The file's context menu can open the file or show it in File Explorer.

This is a list of the files in the folders you chose, not a download log across all browsers. Unfinished downloads are filtered out heuristically. For older files the creation date approximates the order; for new ones the discovery date is kept. Settings and index live in `%LOCALAPPDATA%\DownloadsStack`.

### Readable names on any wallpaper

Each name gets its own dark backdrop, fitted to the visible text, with blurred edges. The panel's own background stays transparent.

The opacity of that backdrop is set by a slider in the settings, from 0 to 100% (85% by default). The panel repaints immediately, no restart needed; the value is stored in `settings.json` as `backdropOpacity`.

The whole area of the panel, including the gaps between rows and the margin around them, takes the mouse: in a layered window a pixel without alpha lets the cursor through to the window behind, so the surface is filled with an all but invisible `#01000000` background.

Once a drag finishes or is cancelled, the panel hides; the next click on the tray icon opens it again. The mouse capture is released before the native drag and after it. The full path appears only after 2 seconds of hovering over one row; moving to another row means another 2 seconds. Tooltips are off during a drag and on a hidden panel.

### Thumbnails

For PNG, JPG/JPEG and MP4 the system thumbnail — the image itself, or a frame of the video — is shown instead of the generic icon. Loading happens in the background and only for visible files; if Windows cannot produce a thumbnail, the ordinary icon is shown. Row sizes and the hover animation are unchanged.

### The Shell context menu

A right click on a file opens the real classic Windows Shell context menu, with the system commands and the installed extensions. On Windows 11 this is the "Show more options" menu, not File Explorer's new compact one. The outline and the enlarged icon stay while the menu is open. Escape dismisses the menu; once a command runs, the list hides.

The native file menu follows the Windows app theme: dark in the dark theme, light in the light one. The setting is checked before every open, so no restart is needed after the theme changes. Under high contrast the system styling is kept.

A right click on the tray icon opens a compact menu with "Folder settings" and "Exit". It has no separators, and uses a #202020 background, a thin border and padding in the style of the Windows dark system menu.

### Animations and the tray icon

On hover the icon grows to 135% with a soft spring and lifts by 2 DIP, then returns over 120 ms. Background highlighting of the row on hover and selection is gone; the local backdrop behind the name stays. The path appears after 1 second on each file. The panel appears with a slight lift and scale over 170 ms and disappears over 95 ms. Reopening quickly cancels a close that has not finished. During a drag all movement of the panel is stopped.

Animations ask the current monitor for its refresh rate. Content is cached while opening and closing; the text backdrops are cached as well, so the blur is not recomputed every frame. The actual frame rate is up to WPF/DWM and the load, especially for a transparent window.

The tray icon shows the state: white when the list is closed, green #25BC96 when it is open. On close — including Escape, a click outside and the end of a drag — the colour goes back to white. The icon's tooltip switches between "Open the list" and "Close the list". The application icon remains separate.

## Starting with Windows

The settings have a "Startup" group with a "Start with Windows" checkbox. It writes a `DownloadsStack` value into `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`: the full path to the current exe in quotes plus the `--autostart` switch. No administrator rights are required, no service and no scheduled task are created, and the entry shows up in Task Manager's Startup apps alongside every other program. It works identically in the portable and in the installed package.

The registry itself is the source of truth, not `settings.json`. The same switch exists in Task Manager, and a copy of the value in the settings file would start lying the second the user touched it. That is why the settings window re-reads the registry every time it opens.

Windows can disable the entry without deleting it: the command stays where it is, and the user's answer is kept separately, under `Explorer\StartupApproved\Run`. In that case the checkbox honestly stays on — the entry is there, after all — and a line appears underneath it explaining that Windows disabled startup in Task Manager and that it can only be turned back on there. The application does not overwrite that decision.

A portable copy that was moved to another folder repairs itself: on startup it compares the path in the entry and, if that file is gone, rewrites the entry to point at itself. If the file at the old path is still there, the entry is left alone — one run of a second copy should not take startup away from the one the user registered.

From a script the windowless commands do the same: `"Downloads Stack.exe" --autostart-on` and `--autostart-off`. The `--quit` command stops the running instance and only returns once the process has actually closed — that is how the installer frees the exe before replacing it.

## Releases: portable build and installer

`scripts/package.ps1` builds both packages (running the tests first; `-SkipTests` skips them):

- `artifacts/DownloadsStack-<version>-portable-win-x64.zip` — one executable inside, about 61 MB to download. Unpack it anywhere and run the exe; nothing appears in the system until startup is turned on for the first time. The bundle costs nothing to load compared with the ordinary folder, and the shortcut the application writes next to itself takes its icon from the executable, so nothing has to travel alongside it. The numbers are in `CHECKS.md`.
- `artifacts/DownloadsStack-<version>-win-x64.msi` — a per-machine wizard built on WiX's `WixUI_InstallDir` dialog set: welcome, licence, install folder, install. It puts a Start menu shortcut with the same `AppUserModel.ID` the application uses, and launches it at the end. Upgrading over an installed version first asks the running instance to close (`--quit`), which is why it needs no reboot.

The folder the wizard browses to is the parent of the application's own. Windows Installer's browse dialog replaces the whole path with whatever is picked, so browsing to `D:\Tools` with the application folder as the target would put four hundred files straight into `D:\Tools`; with the parent as the target it becomes `D:\Tools\Downloads Stack`, which is what picking a folder is taken to mean.

Version 1.1.0 offered a choice between installing for one user and for everybody, through `WixUI_Advanced`. The choice did not work: the first thing that dialog set's Next button does is `WixAppFolder = "WixPerUserFolder"` when `NOT Privileged`, so a package started by a double click — never elevated — puts the answer back to "only for me" before reading it. Choosing "for all users" produced an AppData path. Making the package per-machine hands elevation to Windows, which does it properly; anyone who wants no administrator rights has the portable build.

Program Files is read-only to an ordinary user, so the shortcut the application writes next to its own executable cannot be written into an installed copy. That is not an error and is not reported as one: the installation carries a Start menu shortcut with the same identity, which is what the one beside the executable exists to provide in a portable copy, where nobody else provides it.

Startup stays a per-user switch. Installing for all users puts the program on the machine; it does not start it for everybody. Each user turns the checkbox on for themselves.

Startup is turned on the same way in both packages — the checkbox in the settings. The installer does not touch the `Run` key at all, and that is a measured decision rather than an oversight: a registry change made inside a Windows Installer transaction did not survive that transaction — neither one written by the package itself, nor one written by the program the package launched from a custom action. The details and the numbers are in `CHECKS.md`.

The reverse follows from it: uninstalling does not remove the startup entry if the user had turned it on. Clear the checkbox before uninstalling, or remove the line in Task Manager's Startup apps; an entry pointing at a deleted file is simply ignored by Windows.

```powershell
msiexec /i "artifacts\DownloadsStack-1.2.0-win-x64.msi" /qn
msiexec /x "artifacts\DownloadsStack-1.2.0-win-x64.msi" /qn
```

## Building

Windows 11 and the .NET SDK 10 are required. The self-contained build includes the runtime.

```powershell
dotnet test -c Release
dotnet publish src/DownloadsStack/DownloadsStack.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o artifacts/win-x64
```

Publish runs crossgen (`PublishReadyToRun`), so it takes noticeably longer than an ordinary build; at application startup that saves most of the JIT work. WinForms is not used: the tray icon is registered through `Shell_NotifyIcon` directly.

The installer is built by WiX 6, wired up as a local tool of the repository: `dotnet tool restore` is its entire toolchain, and `package.ps1` does that itself. The package markup is `packaging/DownloadsStack.wxs`. The version number lives in one place, in `<Version>` in `src/DownloadsStack/DownloadsStack.csproj`: both the archive name and the installer's upgrade logic read it from there.

`Theme.xaml` holds the shared styles. `TrayService` is the tray icon. `MainWindow` covers opening and closing, positioning and dragging. `DownloadsService` watches the folders. `ShellService` performs the Windows file operations.

The checks and the limitations are in `CHECKS.md`. `scripts/smoke-lifecycle.ps1` was rewritten for tray mode: start without a window, delivery of a real click on the icon after re-registering with the shell, opening the list from a second launch, closing back into the tray, a single instance, the shortcut with its AppUserModel.ID, and no entries in `app.log` over the run. It also checks the installer's commands: that `--quit` really stops the process, that `--autostart-on` writes the expected line into `Run`, and that `--autostart-off` removes it. Whatever way the run ends, the script puts that line's previous value on the machine back.
