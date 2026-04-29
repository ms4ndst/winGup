# winGup — Winget Updater

A .NET 8 WinForms system tray app that monitors `winget` for available package updates and lets you install them with one click.

## Features

- **System tray icon** — Catppuccin Green (no updates) or Red with count (updates available)
- **Update list window** — shows all available updates with name, current and available version, source, and pin status
- **Update Selected** — installs chosen packages
- **Update All** — installs all unpinned packages in one go
- **Toggle Pin** — pin/unpin packages to exclude them from bulk updates
- **Scheduled checks** — morning (08:00) and afternoon (16:00) by default
- **Configurable** — INI file at `%LOCALAPPDATA%\WingetUpdater\settings.ini`
- **Runs as Windows Service or standalone** — both modes share the same IPC protocol

## Architecture

```
src/WinGup/
├── Program.cs                      # Entry point — service / standalone / ui modes
├── WingetUpdaterService.cs         # BackgroundService: runs update loop, hosts IPC server
├── UpdateChecker.cs                # Runs winget, parses output, manages pin state
├── IUpdateChecker.cs               # Interface + CheckCompleted event
├── TrayApplication.cs              # NotifyIcon, context menu, icon color logic
├── UpdateListWindow.cs             # DataGridView UI — list, update, pin
├── UpdateCountChangedEventArgs.cs  # EventArgs for UpdateCountChanged event
├── SettingsWindow.cs               # Settings editor
├── WindowManager.cs                # Opens / reloads windows
├── CatppuccinTheme.cs              # Catppuccin Mocha design system — colors, form/control helpers
├── IpcServer.cs                    # Named pipe server
├── IpcClient.cs                    # Named pipe client (message-mode, streamed read)
├── IIpcClient.cs                   # IPC client interface
├── ConfigManager.cs                # INI config with defaults
├── IConfigManager.cs               # Config interface
└── Models/
    ├── UpdateInfo.cs               # readonly record struct for one update entry
    ├── IpcMessage.cs               # IPC message envelope
    └── ServiceStatus.cs            # Service status enum
```

Named pipes run in `PipeTransmissionMode.Message`. The client reads in a loop until `IsMessageComplete` to handle responses larger than 4 KB.

## Building

```bash
dotnet build WinGup.slnx
```

## Running

### Standalone (tray app + background checker in one process)
```bash
winGup.exe --standalone
```

### As a Windows Service
```bash
# Install
sc create WinGup binPath="C:\path\to\winGup.exe --service"
sc start WinGup

# The UI connects via IPC
winGup.exe --ui
```

## Testing

```bash
dotnet test tests/WinGup.Tests
```

## Configuration

`%LOCALAPPDATA%\WingetUpdater\settings.ini`:

```ini
[Settings]
morning_check_time = 08:00
afternoon_check_time = 16:00
notify_on_updates = true
auto_check = true
include_pinned_updates = false
include_unknown_versions = false
```

## UI Theme

winGup uses the [Catppuccin Mocha](https://github.com/catppuccin/catppuccin) color palette. All surfaces, text, interactive states, and the tray icon are styled through `CatppuccinTheme.cs`:

| Role | Color | Hex |
|---|---|---|
| Background | Base | `#1e1e2e` |
| Panels / modals | Mantle | `#181825` |
| Terminal output | Crust | `#11111b` |
| Primary action | Mauve | `#cba6f7` |
| Body text | Text | `#cdd6f4` |
| Muted text | Subtext1 | `#bac2de` |
| No-updates icon | Green | `#a6e3a1` |
| Updates-available icon | Red | `#f38ba8` |

The title bar is switched to dark mode via `DwmSetWindowAttribute` on Windows 10 20H1+ and Windows 11.

## Requirements

- .NET 8 Runtime
- Windows 10 / 11
- Windows Package Manager (`winget`)

## License

MIT
