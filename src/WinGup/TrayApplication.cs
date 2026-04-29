using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace WinGup;

/// <summary>
/// System tray application ported from Python system_tray.py
/// Uses WinForms NotifyIcon with dynamic icon generation
/// </summary>
public class TrayApplication : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly IIpcClient _ipcClient;
    private readonly IUpdateChecker? _updateChecker;
    private readonly ILogger _logger;
    private UpdateListWindow? _updateWindow;
    private SettingsWindow? _settingsWindow;
    private int _updateCount;
    private volatile bool _manualCheckPending;
    private CancellationTokenSource? _cts;
    private SynchronizationContext? _uiSyncContext;

    /// <summary>
    /// Creates a new TrayApplication
    /// </summary>
    /// <param name="ipcClient">IPC client for service communication</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="updateChecker">Optional direct update checker (standalone mode — bypasses IPC polling)</param>
    public TrayApplication(IIpcClient ipcClient, ILogger logger, IUpdateChecker? updateChecker = null)
    {
        _ipcClient = ipcClient ?? throw new ArgumentNullException(nameof(ipcClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _updateChecker = updateChecker;


        _contextMenu = CreateContextMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = GenerateIcon(0),
            Visible = true,
            Text = "Winget Updater",
            ContextMenuStrip = _contextMenu
        };

        _notifyIcon.DoubleClick += (_, _) => ShowUpdates();
    }

    /// <summary>
    /// Starts the tray application message loop
    /// </summary>
    public void Run()
    {
        _cts = new CancellationTokenSource();
        _uiSyncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _logger.LogInformation("Tray application started");

        if (_updateChecker is not null)
            _updateChecker.CheckCompleted += OnCheckCompleted;

        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(3000, "Winget Updater", "Service is running", ToolTipIcon.Info);

        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await ListenForMessagesAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogError(ex, "Error in IPC listener"); }
        }, token);

        _logger.LogInformation("Starting WinForms message loop");
        Application.Run();
        _logger.LogInformation("WinForms message loop ended");
    }

    /// <summary>
    /// Updates the tray icon with the current update count
    /// </summary>
    /// <param name="updateCount">Number of available updates</param>
    public void UpdateIcon(int updateCount)
    {
        _updateCount = updateCount;
        _notifyIcon.Icon = GenerateIcon(updateCount);

        var text = updateCount > 0
            ? $"Winget Updater - {updateCount} update(s) available"
            : "Winget Updater - No updates";

        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private ContextMenuStrip CreateContextMenu()
    {
        var menu = new ContextMenuStrip();

        var checkNow    = new ToolStripMenuItem("Check for Updates", null, (_, _) => CheckNow());
        var showUpdates = new ToolStripMenuItem("Show Updates",      null, (_, _) => ShowUpdates());
        var settings    = new ToolStripMenuItem("Settings",          null, (_, _) => ShowSettings());
        var separator   = new ToolStripSeparator();
        var exit        = new ToolStripMenuItem("Exit",              null, (_, _) => Exit());

        menu.Items.AddRange([checkNow, showUpdates, settings, separator, exit]);
        CatppuccinTheme.StyleContextMenu(menu);
        return menu;
    }

    private static Icon GenerateIcon(int count)
    {
        const int size = 16;
        using var bitmap = new Bitmap(size, size);
        using var graphics = Graphics.FromImage(bitmap);

        var fillColor = count > 0 ? CatppuccinTheme.Red : CatppuccinTheme.Green;
        using var fillBrush    = new SolidBrush(fillColor);
        using var outlinePen   = new Pen(CatppuccinTheme.Crust);
        using var countBrush   = new SolidBrush(CatppuccinTheme.Crust);

        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillEllipse(fillBrush,  0, 0, size - 1, size - 1);
        graphics.DrawEllipse(outlinePen, 0, 0, size - 1, size - 1);

        if (count > 0)
        {
            var text = count > 9 ? "9+" : count.ToString();
            using var font = new Font("Arial", 8, FontStyle.Bold);
            var textSize = graphics.MeasureString(text, font);
            var x = (size - textSize.Width) / 2;
            var y = (size - textSize.Height) / 2 - 1;
            graphics.DrawString(text, font, countBrush, x, y);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    private void CheckNow()
    {
        _manualCheckPending = true;
        InvokeOnUiThread(() => _notifyIcon.ShowBalloonTip(3000, "Winget Updater", "Checking for updates...", ToolTipIcon.Info));

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Manual check requested from tray");
                await _ipcClient.SendMessageAsync("check_updates").ConfigureAwait(false);

                // Standalone mode result is delivered via OnCheckCompleted; IPC mode needs polling.
                if (_updateChecker is null)
                {
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        var status = await _ipcClient.SendMessageAsync("get_status").ConfigureAwait(false);
                        if (status is null) break;
                        var parsed = ParseStatus(status);
                        if (parsed.Status != "checking")
                        {
                            _manualCheckPending = false;
                            InvokeOnUiThread(() =>
                            {
                                UpdateIcon(parsed.UpdateCount);
                                var msg = parsed.UpdateCount > 0
                                    ? $"{parsed.UpdateCount} update(s) available."
                                    : "No updates found.";
                                _notifyIcon.ShowBalloonTip(5000, "Winget Updater", msg,
                                    parsed.UpdateCount > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
                            });
                            return;
                        }
                    }
                    _manualCheckPending = false;
                }
            }
            catch (Exception ex)
            {
                _manualCheckPending = false;
                _logger.LogError(ex, "Failed to send check_updates command");
            }
        });
    }

    private void ShowUpdates()
    {
        if (_updateWindow is { IsDisposed: false })
        {
            _updateWindow.BringToFront();
            return;
        }

        try
        {
            _logger.LogInformation("Showing updates window");
            _updateWindow = new UpdateListWindow(_ipcClient);
            _updateWindow.UpdateCountChanged += (_, e) => InvokeOnUiThread(() => UpdateIcon(e.Count));
            _updateWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to show updates window");
            MessageBox.Show("Failed to load updates: " + ex.Message, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsDisposed: false })
        {
            _settingsWindow.BringToFront();
            return;
        }

        _settingsWindow = new SettingsWindow(_ipcClient, _logger);
        _settingsWindow.Show();
    }

    private void Exit()
    {
        _logger.LogInformation("Exit requested from tray");
        _cts?.Cancel();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    private void OnCheckCompleted(object? sender, EventArgs e)
    {
        InvokeOnUiThread(() =>
        {
            var count = _updateChecker!.UpdateCount;
            UpdateIcon(count);

            if (_manualCheckPending)
            {
                _manualCheckPending = false;
                var msg = count > 0 ? $"{count} update(s) available." : "No updates found.";
                _notifyIcon.ShowBalloonTip(5000, "Winget Updater", msg,
                    count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        });
    }

    private async Task ListenForMessagesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                int count;
                if (_updateChecker is not null)
                {
                    // Standalone mode: read directly — no IPC round-trip needed
                    count = _updateChecker.UpdateCount;
                }
                else
                {
                    var message = await _ipcClient.SendMessageAsync("get_status").ConfigureAwait(false);
                    count = message is not null ? ParseStatus(message).UpdateCount : _updateCount;
                }

                InvokeOnUiThread(() => UpdateIcon(count));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling status");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
        }
    }

    private static (int UpdateCount, string Status) ParseStatus(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var count = root.TryGetProperty("update_count", out var el) ? el.GetInt32() : 0;
            var checking = root.TryGetProperty("is_checking", out var chk) && chk.GetBoolean();
            return (count, checking ? "checking" : "idle");
        }
        catch
        {
            return (0, "idle");
        }
    }

    private void InvokeOnUiThread(Action action)
    {
        if (_uiSyncContext is not null)
            _uiSyncContext.Post(_ => action(), null);
        else
            action();
    }

    /// <summary>
    /// Disposes the tray application and cleans up resources
    /// </summary>
    public void Dispose()
    {
        if (_updateChecker is not null)
            _updateChecker.CheckCompleted -= OnCheckCompleted;
        _cts?.Cancel();
        _cts?.Dispose();
        _updateWindow?.Dispose();
        _settingsWindow?.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
    }
}
