# Changelog

## [1.0.5] - 2026-07-07

### Fixed
- Update list window: install/download output pane no longer slides under the bottom button bar. Corrected the WinForms dock add-order so the Fill container is added before the bottom-docked button panel.

## [Unreleased]

### Added
- Initial C# port of Python Winget_Updater
- Core update checking logic with winget subprocess management
- IPC communication via Named Pipes
- Configuration management with INI format
- Windows Service integration using BackgroundService
- System tray integration (pending UI port)
- xunit test suite with FluentAssertions

### Changed
- Replaced Python pywin32 service framework with .NET 8 BackgroundService
- Replaced configparser with custom INI implementation
- Replaced Pillow (Python) with System.Drawing.Common for tray icons
- Replaced argparse with simple switch expression CLI

### Performance
- Using readonly record struct for UpdateInfo (no heap allocation)
- Span-based parsing for winget output
- ArrayPool for IPC buffer recycling
- Server GC enabled

## [0.1.0] - 2026-04-27

### Added
- Project analysis (ANALYSIS.md)
- Design document (DESIGN.md) with memory strategy
- Solution structure with WinGup.slnx
- Core models: UpdateInfo, IpcMessage, ServiceStatus
- UpdateChecker with winget interaction
- ConfigManager with defaults
- IpcServer and IpcClient for IPC
- WingetUpdaterService as BackgroundService
- Program.cs with CLI modes
- 6 passing unit tests

### Known Issues
- MSB9008 warning about solution reference (non-critical)
- NetAnalyzers version mismatch warning (suppressed)
- UI components (TrayApplication, SettingsWindow, UpdateListWindow) not yet ported
