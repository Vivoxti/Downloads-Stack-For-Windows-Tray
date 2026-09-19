# Downloads Stack — a macOS Dock stack for the Windows tray

**An attempt to bring the macOS Dock's Downloads stack to Windows 11.** On a Mac, the Downloads folder sits
in the Dock and fans open into the newest files. Windows has no such thing, so this is it: an icon in the
system tray, next to the clock, that opens a compact list of the newest files from the folders you choose.

[![Release](https://img.shields.io/github/v/release/Vivoxti/Downloads-Stack-For-Windows-Tray?label=release&color=25BC96)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/Vivoxti/Downloads-Stack-For-Windows-Tray/total?label=downloads)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/releases)
[![Build](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/actions/workflows/ci.yml/badge.svg)](https://github.com/Vivoxti/Downloads-Stack-For-Windows-Tray/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

<p align="center">
  <img src="docs/demo.gif" alt="The tray icon is clicked and a transparent list of recent files unfolds above the taskbar; a file is dragged out of it" width="440">
</p>

Windows 11 · .NET 10 · WPF · a self-contained build, with nothing to install beforehand.

It is a list rather than a fan: no stack animation, no grid view, no Dock. What it borrows is the idea —
the newest downloads one click away, without opening File Explorer. The panel is fully transparent, so all
you see are the system file icons and the file names. One click opens a file, dragging hands over a real
Shell data object, a right click brings up the classic File Explorer context menu. There can be several
source folders; Downloads is connected by default.

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

No title, no buttons, no scrolling: the panel shows the newest files that fit completely, the first of them at the bottom, closest to the tray icon. A left click or Enter opens a file, a right click opens the Windows context menu, and dragging hands the file over to whatever accepts it.

<p align="center">
  <img src="docs/settings.png" alt="The settings window: the list of source folders, the sort order, the startup checkbox, and the sliders for how many files to show and how opaque the backdrop behind each name is" width="540">
</p>

A right click on the tray icon opens the settings: the source folders, the sort order, how many files to show, the opacity of the backdrop behind the names, startup, and hardware graphics acceleration. Everything but the acceleration applies at once; that one needs a restart, and leaving it off is what keeps the application at about 16 MB in the tray instead of 62.

Downloads is connected by default, and subfolders are never walked. Removing a source removes its rows, never its files. Sorting is by date added, name, type, or the file's modified, created and accessed dates, in either direction. Settings and index live in `%LOCALAPPDATA%\DownloadsStack`.

This is a list of what is in the folders you chose, not a download log across all browsers, and unfinished downloads are filtered out heuristically.

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

<p align="center">
  <img src="docs/tray.png" alt="The same corner of the tray twice: the icon green while the list is open, and white while it is closed" width="314">
</p>

## Starting with Windows

A "Start with Windows" checkbox in the settings, which writes the executable's path into `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no administrator rights, no service, no scheduled task, and the same switch turns up in Task Manager's Startup apps, where turning it off wins.

## Building

Windows 11 and the .NET SDK 10 are required. The self-contained build includes the runtime.

```powershell
dotnet test -c Release
dotnet publish src/DownloadsStack/DownloadsStack.csproj -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -o artifacts/win-x64
```

Publish runs crossgen (`PublishReadyToRun`), so it takes noticeably longer than an ordinary build; at application startup that saves most of the JIT work. WinForms is not used: the tray icon is registered through `Shell_NotifyIcon` directly.

`scripts/package.ps1` builds the two release packages — the single-executable archive and the installer — running the tests first, which `-SkipTests` skips.

The installer is built by WiX 6, wired up as a local tool of the repository: `dotnet tool restore` is its entire toolchain, and `package.ps1` does that itself. The package markup is `packaging/DownloadsStack.wxs`. The version number lives in one place, in `<Version>` in `src/DownloadsStack/DownloadsStack.csproj`: both the archive name and the installer's upgrade logic read it from there.

`Theme.xaml` holds the shared styles. `TrayService` is the tray icon. `MainWindow` covers opening and closing, positioning and dragging. `DownloadsService` watches the folders. `ShellService` performs the Windows file operations.

The checks and the limitations are in `CHECKS.md`. `scripts/smoke-lifecycle.ps1` was rewritten for tray mode: start without a window, delivery of a real click on the icon after re-registering with the shell, opening the list from a second launch, closing back into the tray, a single instance, the shortcut with its AppUserModel.ID, and no entries in `app.log` over the run. It also checks the installer's commands: that `--quit` really stops the process, that `--autostart-on` writes the expected line into `Run`, and that `--autostart-off` removes it. Whatever way the run ends, the script puts that line's previous value on the machine back.
