namespace WinGup.Models;

/// <summary>
/// Represents the current status of the Winget Updater service.
/// </summary>
/// <param name="UpdateCount">Number of available updates</param>
/// <param name="LastCheck">Timestamp of the last update check</param>
/// <param name="AutoCheck">Whether automatic checking is enabled</param>
/// <param name="MorningCheck">Morning check time (HH:MM format)</param>
/// <param name="AfternoonCheck">Afternoon check time (HH:MM format)</param>
public record class ServiceStatus(
    int UpdateCount,
    DateTime? LastCheck,
    bool AutoCheck,
    string MorningCheck,
    string AfternoonCheck
);
