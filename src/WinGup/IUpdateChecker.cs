using WinGup.Models;

namespace WinGup;

/// <summary>
/// Interface for checking Winget package updates.
/// </summary>
public interface IUpdateChecker : IAsyncDisposable
{
    /// <summary>Raised after each update check completes.</summary>
    event EventHandler? CheckCompleted;

    /// <summary>
    /// Gets a value indicating whether an update check is currently in progress.
    /// </summary>
    bool IsChecking { get; }

    /// <summary>
    /// Gets the count of available updates from the last check.
    /// </summary>
    int UpdateCount { get; }

    /// <summary>
    /// Checks for available updates asynchronously.
    /// </summary>
    /// <param name="force">Force check even if within check interval</param>
    /// <param name="includePinned">Include pinned packages</param>
    /// <param name="includeUnknown">Include packages with unknown versions</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of updates found</returns>
    Task<int> CheckUpdatesAsync(
        bool force = false,
        bool includePinned = false,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the last check time.
    /// </summary>
    DateTime? LastCheckTime { get; }

    /// <summary>
    /// Gets the cached list of updates from the last check.
    /// </summary>
    IReadOnlyList<UpdateInfo> GetCachedUpdates();

    /// <summary>
    /// Toggles the pin state of the given package IDs.
    /// </summary>
    Task TogglePinAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upgrades the specified packages and refreshes the cached update list.
    /// </summary>
    /// <returns>Package IDs that failed to install.</returns>
    Task<IReadOnlyList<string>> UpdatePackagesAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default);
}

