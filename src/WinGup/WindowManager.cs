using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Window manager ported from Python window_manager.py
/// Manages the lifecycle of UI windows (settings, update list)
/// </summary>
public class WindowManager : IDisposable
{
    private readonly IIpcClient _ipcClient;
    private readonly ILogger<WindowManager> _logger;
    private SettingsWindow? _settingsWindow;
    private UpdateListWindow? _updateListWindow;
    private TrayApplication? _trayApp;
    private bool _disposed;

    /// <summary>
    /// Creates a new WindowManager
    /// </summary>
    /// <param name="ipcClient">IPC client for service communication</param>
    /// <param name="logger">Logger instance</param>
    public WindowManager(IIpcClient ipcClient, ILogger<WindowManager> logger)
    {
        _ipcClient = ipcClient ?? throw new ArgumentNullException(nameof(ipcClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the tray application
    /// </summary>
    public void StartTrayApplication()
    {
        if (_trayApp is not null) return;

        _logger.LogInformation("Starting tray application");
        _trayApp = new TrayApplication(_ipcClient, _logger);
        _trayApp.Run();
    }

    /// <summary>
    /// Shows the settings window
    /// </summary>
    public void ShowSettings()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.BringToFront();
            return;
        }

        _logger.LogInformation("Opening settings window");
        _settingsWindow = new SettingsWindow(_ipcClient, _logger);
        _settingsWindow.Show();
    }

    /// <summary>
    /// Shows the update list window
    /// </summary>
    /// <param name="updates">Optional predefined updates to display</param>
    public void ShowUpdateList(IEnumerable<UpdateInfo>? updates = null)
    {
        if (_updateListWindow is { IsDisposed: false })
        {
            _updateListWindow.BringToFront();
            _updateListWindow.Reload();
            return;
        }

        _logger.LogInformation("Opening update list window");

        if (updates is not null)
        {
            _updateListWindow = new UpdateListWindow(updates);
        }
        else
        {
            _updateListWindow = new UpdateListWindow(_ipcClient);
        }

        _updateListWindow.Show();
    }

    /// <summary>
    /// Updates the tray icon with the current update count
    /// </summary>
    /// <param name="updateCount">Number of available updates</param>
    public void UpdateTrayIcon(int updateCount)
    {
        if (_trayApp is null) return;
        _trayApp.UpdateIcon(updateCount);
    }

    /// <summary>
    /// Disposes the WindowManager and cleans up resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing WindowManager");

        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.Invoke(() =>
            {
                _settingsWindow?.Close();
                _settingsWindow?.Dispose();
            });
        }

        if (_updateListWindow is { IsDisposed: false })
        {
            _updateListWindow.Invoke(() =>
            {
                _updateListWindow?.Close();
                _updateListWindow?.Dispose();
            });
        }

        _trayApp?.Dispose();
    }
}
