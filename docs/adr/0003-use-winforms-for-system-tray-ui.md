# ADR-0003: Use WinForms for System Tray UI

## Status
Accepted

## Context
The Python Winget_Updater uses:
- `pystray` library for system tray icon
- `Pillow` (PIL) for generating dynamic tray icons with update count overlay
- Tkinter for settings and update list windows

We need to port this to C# for Windows-only deployment. Options considered:

1. **WPF with Hardcodet.Wpf.TaskbarNotification** - Modern, but adds dependency
2. **WinForms NotifyIcon** - Built-in, mature, zero additional dependencies
3. **Avalonia with TrayIcon** - Cross-platform, but overkill for Windows-only app
4. **Custom Win32 interop** - Too complex, reinventing the wheel

For icon generation:
1. **System.Drawing.Common** - Built-in, compatible with .NET 8 (Windows only)
2. **ImageSharp** - Cross-platform, but adds dependency
3. **SkiaSharp** - Google's graphics library, but overkill

For UI windows:
1. **WinForms** - Simple, matches NotifyIcon choice
2. **WPF** - More modern, but mixing with WinForms tray is odd
3. **Avalonia** - Cross-platform, unnecessary for Windows-only

## Decision
We will use:
- **WinForms NotifyIcon** for system tray integration
- **System.Drawing.Common** for dynamic icon generation (drawing update count)
- **WinForms Forms** for settings and update list windows

## Consequences

### Positive
- Zero additional NuGet dependencies (all built-in for Windows)
- Familiar API for Windows developers
- System.Drawing.Common is mature and well-documented
- WinForms designer can be used for UI (if desired)
- Consistent with Windows-only target (`net8.0-windows10.0.17763.0`)

### Negative
- WinForms is "legacy" (but still supported and appropriate here)
- System.Drawing.Common is Windows-only (which is fine for our use case)
- UI will look "classic Windows" (not modern Fluent design)

### Neutral
- Python's `pystray` → WinForms `NotifyIcon` is functionally equivalent
- Python's `Pillow` → `System.Drawing.Common` provides same capabilities
- Python's Tkinter → WinForms Forms is a reasonable match

## Implementation Notes

### TrayApplication.cs
```csharp
using System.Drawing;
using System.Windows.Forms;

public class TrayApplication : IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplication()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Winget Updater"
        };

        // Context menu
        _notifyIcon.ContextMenuStrip = CreateContextMenu();
    }

    public void UpdateIcon(int updateCount)
    {
        // Generate icon with overlay using System.Drawing
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        // ... draw base icon and update count
        _notifyIcon.Icon = Icon.FromHandle(bitmap.GetHicon());
    }
}
```

### SettingsWindow.cs
```csharp
using System.Windows.Forms;

public class SettingsWindow : Form
{
    // CheckBox for notify_on_updates, auto_check, etc.
    // DateTimePicker for morning/afternoon check times
    // Button for Save/Cancel
}
```

### UpdateListWindow.cs
```csharp
using System.Windows.Forms;

public class UpdateListWindow : Form
{
    private readonly DataGridView _grid;

    public UpdateListWindow(IEnumerable<UpdateInfo> updates)
    {
        _grid = new DataGridView
        {
            DataSource = updates.ToList()
        };
        Controls.Add(_grid);
    }
}
```

## References
- [WinForms NotifyIcon documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon)
- [System.Drawing.Common in .NET 8](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only)
- Python original: `system_tray.py`, `ui_component.py`, `window_manager.py`
