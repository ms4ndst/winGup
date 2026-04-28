namespace WinGup.Models;

/// <summary>
/// Represents a Winget package update with current and available versions.
/// </summary>
/// <param name="Name">Display name of the package</param>
/// <param name="Id">Winget package identifier</param>
/// <param name="CurrentVersion">Currently installed version</param>
/// <param name="AvailableVersion">Version available for upgrade</param>
/// <param name="Source">Source repository (e.g., "winget")</param>
/// <param name="IsPinned">Whether the package is pinned</param>
public readonly record struct UpdateInfo(
    string Name,
    string Id,
    string CurrentVersion,
    string AvailableVersion,
    string Source = "winget",
    bool IsPinned = false
);
