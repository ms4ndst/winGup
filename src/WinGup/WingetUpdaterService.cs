using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Windows Service implementation for Winget Updater.
/// </summary>
/// <remarks>
/// Uses .NET 8 Generic Host with Windows Service lifetime.
/// </remarks>
public partial class WingetUpdaterService : BackgroundService
{
    private readonly ILogger<WingetUpdaterService> _logger;
    private readonly IConfigManager _configManager;
    private readonly IUpdateChecker _updateChecker;
    private readonly IpcServer _ipcServer;
    private Thread? _schedulerThread;
    private bool _running = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="WingetUpdaterService"/> class.
    /// </summary>
    public WingetUpdaterService(
        ILogger<WingetUpdaterService> logger,
        IConfigManager configManager,
        IUpdateChecker updateChecker,
        IpcServer ipcServer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        _ipcServer = ipcServer ?? throw new ArgumentNullException(nameof(ipcServer));

        RegisterCommandHandlers();
    }

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Winget Updater Service starting");
        _ipcServer.Start();
        _logger.LogInformation("IPC server started successfully");
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Winget Updater Service started");

        await _updateChecker.CheckUpdatesAsync(cancellationToken: stoppingToken).ConfigureAwait(false);

        _schedulerThread = new Thread(() => RunScheduler(stoppingToken))
        {
            IsBackground = true,
            Name = "Scheduler Thread"
        };
        _schedulerThread.Start();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping
        }

        _running = false;
        _logger.LogInformation("Winget Updater Service stopped");
    }

    /// <inheritdoc/>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service stop requested");
        _running = false;
        _ipcServer.Stop();
        return base.StopAsync(cancellationToken);
    }

    private void RunScheduler(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scheduler thread started");

        while (_running && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_configManager.AutoCheck)
                {
                    var now = DateTime.Now;
                    var currentTime = now.ToString("HH:mm");

                    if (currentTime == _configManager.MorningCheckTime ||
                        currentTime == _configManager.AfternoonCheckTime)
                    {
                        _logger.LogInformation("Scheduled update check at {CurrentTime}", currentTime);
                        _updateChecker.CheckUpdatesAsync(cancellationToken: cancellationToken)
                            .GetAwaiter().GetResult();
                    }
                }

                Thread.Sleep(30000);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in scheduler thread: {Exception}", ex);
                Thread.Sleep(60000);
            }
        }
    }

    private void RegisterCommandHandlers()
    {
        _ipcServer.RegisterHandler("check_updates", HandleCheckUpdates);
        _ipcServer.RegisterHandler("get_status", HandleGetStatus);
        _ipcServer.RegisterHandler("get_updates", HandleGetUpdates);
        _ipcServer.RegisterHandler("get_last_check", HandleGetLastCheck);
        _ipcServer.RegisterHandler("save_settings", HandleSaveSettings);
        _ipcServer.RegisterHandler("get_settings", HandleGetSettings);
        _ipcServer.RegisterHandler("toggle_pin", HandleTogglePin);
        _ipcServer.RegisterHandler("update_packages", HandleUpdatePackages);
    }

    private string? HandleGetStatus(string? data)
    {
        var response = new
        {
            update_count = _updateChecker.UpdateCount,
            last_check = _updateChecker.LastCheckTime?.ToString("o"),
            is_checking = _updateChecker.IsChecking
        };
        return System.Text.Json.JsonSerializer.Serialize(response);
    }

    private string? HandleGetUpdates(string? data)
    {
        var updates = _updateChecker.GetCachedUpdates();
        return System.Text.Json.JsonSerializer.Serialize(updates);
    }

    private string? HandleGetLastCheck(string? data)
    {
        return _updateChecker.LastCheckTime?.ToString("o") ?? "";
    }

    private string? HandleCheckUpdates(string? data)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _updateChecker.CheckUpdatesAsync(force: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking updates");
            }
        }).ConfigureAwait(false);
        return null;
    }

    private string? HandleSaveSettings(string? data)
    {
        try
        {
            if (!string.IsNullOrEmpty(data))
            {
                using var document = System.Text.Json.JsonDocument.Parse(data);
                var root = document.RootElement;

                if (root.TryGetProperty("morning_check", out var morning))
                    _configManager.MorningCheckTime = morning.GetString() ?? "";

                if (root.TryGetProperty("afternoon_check", out var afternoon))
                    _configManager.AfternoonCheckTime = afternoon.GetString() ?? "";

                if (root.TryGetProperty("notify_on_updates", out var notify))
                    _configManager.NotifyOnUpdates = notify.GetBoolean();

                if (root.TryGetProperty("auto_check", out var autoCheck))
                    _configManager.AutoCheck = autoCheck.GetBoolean();

                _logger.LogInformation("Settings updated via IPC");
            }

            return System.Text.Json.JsonSerializer.Serialize(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError("Error saving settings: {Exception}", ex);
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private string? HandleGetSettings(string? data)
    {
        var response = new
        {
            morning_check = _configManager.MorningCheckTime,
            afternoon_check = _configManager.AfternoonCheckTime,
            notify_on_updates = _configManager.NotifyOnUpdates,
            auto_check = _configManager.AutoCheck,
            last_check = _configManager.LastCheck?.ToString("o")
        };

        return System.Text.Json.JsonSerializer.Serialize(response);
    }

    private string? HandleUpdatePackages(string? data)
    {
        if (string.IsNullOrEmpty(data)) return null;

        try
        {
            var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(data) ?? new();
            var failed = _updateChecker.UpdatePackagesAsync(ids).GetAwaiter().GetResult();
            var result = new
            {
                success = true,
                failed = failed,
                updates = _updateChecker.GetCachedUpdates()
            };
            return System.Text.Json.JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating packages");
            return System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.Message });
        }
    }

    private string? HandleTogglePin(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        try
        {
            var ids = System.Text.Json.JsonSerializer.Deserialize<List<string>>(data) ?? new();
            _updateChecker.TogglePinAsync(ids).GetAwaiter().GetResult();
            _logger.LogInformation("Toggled pin for {Count} package(s)", ids.Count);
            return System.Text.Json.JsonSerializer.Serialize(_updateChecker.GetCachedUpdates());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling pin");
            return null;
        }
    }
}
