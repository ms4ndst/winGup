namespace WinGup;

/// <summary>
/// Interface for managing application configuration.
/// </summary>
public interface IConfigManager
{
    /// <summary>
    /// Gets or sets the morning check time in HH:MM format.
    /// </summary>
    string MorningCheckTime { get; set; }

    /// <summary>
    /// Gets or sets the afternoon check time in HH:MM format.
    /// </summary>
    string AfternoonCheckTime { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to notify on updates.
    /// </summary>
    bool NotifyOnUpdates { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to automatically check for updates.
    /// </summary>
    bool AutoCheck { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include pinned updates in update checks.
    /// </summary>
    bool IncludePinnedUpdates { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include packages with unknown versions.
    /// </summary>
    bool IncludeUnknownVersions { get; set; }

    /// <summary>
    /// Gets or sets the last check time.
    /// </summary>
    DateTime? LastCheck { get; set; }

    /// <summary>
    /// Sets the last check time to now.
    /// </summary>
    void SetLastCheck();

    /// <summary>
    /// Sets the last check time to a specific value.
    /// </summary>
    /// <param name="value">The timestamp to set</param>
    void SetLastCheck(DateTime value);

    /// <summary>
    /// Saves the current configuration to the config file.
    /// </summary>
    void Save();

    /// <summary>
    /// Loads the configuration from the config file.
    /// </summary>
    void Load();
}
