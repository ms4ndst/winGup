using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Checks for available Winget package updates by running winget CLI commands.
/// </summary>
/// <remarks>
/// This class handles running winget subprocess, parsing output (JSON or text format),
/// and managing cached update information.
/// </remarks>
public partial class UpdateChecker : IUpdateChecker
{
    private readonly ILogger<UpdateChecker> _logger;
    private readonly IConfigManager _configManager;
    private readonly List<UpdateInfo> _availableUpdates = new();
    private readonly HashSet<string> _pinnedPackages = new();
    private int _updateCount;
    private DateTime? _lastCheckTime;
    private bool _isChecking;
    private bool _disposed;
    private bool _jsonSupported = true;

    /// <inheritdoc/>
    public event EventHandler? CheckCompleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateChecker"/> class.
    /// </summary>
    /// <param name="configManager">Configuration manager for settings</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public UpdateChecker(IConfigManager configManager, ILogger<UpdateChecker> logger)
    {
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsChecking => _isChecking;

    /// <inheritdoc/>
    public int UpdateCount => _updateCount;

    /// <inheritdoc/>
    public IReadOnlyList<UpdateInfo> AvailableUpdates => _availableUpdates.AsReadOnly();

    /// <inheritdoc/>
    public DateTime? LastCheckTime => _lastCheckTime;

    /// <inheritdoc/>
    public IReadOnlyList<UpdateInfo> GetCachedUpdates() => _availableUpdates.AsReadOnly();

    /// <inheritdoc/>
    public async Task<int> CheckUpdatesAsync(
        bool force = false,
        bool includePinned = false,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
            if (_isChecking && !force)
            {
                _logger.LogInformation("Update check already in progress, skipping");
                return _updateCount;
            }

        if (_configManager != null)
        {
            if (!includePinned) includePinned = _configManager.IncludePinnedUpdates;
            if (!includeUnknown) includeUnknown = _configManager.IncludeUnknownVersions;
        }

        _logger.LogDebug("Update check settings - includePinned: {IncludePinned}, includeUnknown: {IncludeUnknown}", includePinned, includeUnknown);

        _isChecking = true;
                _logger.LogInformation("Starting update check");

        try
        {
            _availableUpdates.Clear();
            _updateCount = 0;

            await RefreshPinnedPackagesAsync(cancellationToken).ConfigureAwait(false);

            var baseCommand = new[]
            {
                "update",
                "--include-unknown",
                "--include-pinned",
                "--accept-source-agreements"
            };

            if (_jsonSupported)
            {
                var result = await TryCheckUpdatesJsonAsync(baseCommand, includePinned, includeUnknown, cancellationToken)
                    .ConfigureAwait(false);

                if (result.HasValue)
                {
                    _lastCheckTime = DateTime.Now;
                    CheckCompleted?.Invoke(this, EventArgs.Empty);
                    _isChecking = false;
                    return result.Value;
                }

                _jsonSupported = false;
                _logger.LogWarning("JSON format not supported by this winget version, using text parsing permanently");
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = string.Join(" ", baseCommand),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                _logger.LogError("Winget update command failed with return code {ReturnCode}: {Error}", process.ExitCode, error);
                _isChecking = false;
                return 0;
            }

            _logger.LogDebug("Winget output length: {Length}, first 500 chars: {Output}", output.Length, output.Substring(0, Math.Min(500, output.Length)));

            ParseWingetOutput(output.AsSpan(), includePinned, includeUnknown);

            _logger.LogInformation("After parsing: Found {UpdateCount} updates", _updateCount);
            
            if (_configManager != null)
            {
                _configManager.SetLastCheck(DateTime.Now);
            }
            _lastCheckTime = DateTime.Now;
            _logger.LogInformation("Update check completed. Found {UpdateCount} updates.", _updateCount);

            CheckCompleted?.Invoke(this, EventArgs.Empty);
            return _updateCount;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking for updates: {Exception}", ex);
            return 0;
        }
        finally
        {
            _isChecking = false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<UpdateInfo>> GetUpdatesListAsync(
        bool includePinned = false,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
        if (_configManager != null)
        {
            if (!includePinned) includePinned = _configManager.IncludePinnedUpdates;
            if (!includeUnknown) includeUnknown = _configManager.IncludeUnknownVersions;
        }

        if (_availableUpdates.Count == 0)
        {
            await CheckUpdatesAsync(force: true, includePinned, includeUnknown, cancellationToken)
                .ConfigureAwait(false);
        }

        return _availableUpdates.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task<int> GetUpdateCountAsync(
        bool includePinned = false,
        bool includeUnknown = false,
        CancellationToken cancellationToken = default)
    {
        if (_configManager != null)
        {
            if (!includePinned) includePinned = _configManager.IncludePinnedUpdates;
            if (!includeUnknown) includeUnknown = _configManager.IncludeUnknownVersions;
        }

        if (_updateCount == 0)
        {
            await CheckUpdatesAsync(force: true, includePinned, includeUnknown, cancellationToken)
                .ConfigureAwait(false);
        }

        return _updateCount;
    }

    /// <inheritdoc/>
    public async Task<bool> InstallAllUpdatesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting installation of all updates");

        await CheckUpdatesAsync(force: true, includePinned: true, includeUnknown: true, cancellationToken)
            .ConfigureAwait(false);

        if (_availableUpdates.Count == 0)
        {
                _logger.LogInformation("No updates to install");
            return true;
        }

        var updatesToInstall = _availableUpdates.ToList();

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "upgrade --all --accept-source-agreements --disable-interactivity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                    _logger.LogInformation("Installation completed successfully");
                await CheckUpdatesAsync(force: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var stillPending = updatesToInstall
                    .Where(u => _availableUpdates.Any(c => c.Id == u.Id))
                    .Select(u => u.Id)
                    .ToList();

                if (stillPending.Count != 0)
                {
                    _logger.LogWarning("Some updates failed to install: {Ids}", string.Join(", ", stillPending));
                    return false;
                }

                _logger.LogInformation("All updates were successfully installed");
                return true;
            }

            _logger.LogError("Installation failed with return code {ReturnCode}", process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during installation: {Exception}", ex);
            return false;
        }
    }

    private async Task<int?> TryCheckUpdatesJsonAsync(
        string[] baseCommand,
        bool includePinned,
        bool includeUnknown,
        CancellationToken cancellationToken)
    {
        var commands = new[]
        {
            baseCommand.Concat(new[] { "--format", "json" }).ToArray(),
            new[] { "winget", "update", "--format", "json" },
            new[] { "winget", "upgrade", "--format", "json" },
            new[] { "winget", "update", "--accept-source-agreements", "--include-unknown", "--include-pinned", "--source", "winget", "--format", "json" }
        };

        foreach (var cmd in commands)
        {
            try
            {
                _logger.LogDebug("Trying JSON command: {Command}", string.Join(" ", cmd));

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = string.Join(" ", cmd.Skip(1)),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode == 0)
                {
                    return ParseWingetJson(output.AsSpan(), includePinned, includeUnknown);
                }

                    _logger.LogDebug("JSON command failed: {Command} with code {ReturnCode}", string.Join(" ", cmd), process.ExitCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("JSON command exception: {Command}: {Exception}", string.Join(" ", cmd), ex);
            }
        }

        return null;
    }

    private int ParseWingetJson(ReadOnlySpan<char> output, bool includePinned, bool includeUnknown)
    {
        try
        {
            _availableUpdates.Clear();

            var outputString = output.ToString();
            using var document = JsonDocument.Parse(outputString);
            var root = document.RootElement;

            if (root.TryGetProperty("Sources", out var sources))
            {
                foreach (var source in sources.EnumerateArray())
                {
                    if (source.TryGetProperty("Packages", out var packages))
                    {
                        ParsePackagesFromJson(packages, includePinned, includeUnknown);
                    }
                }
            }
            else if (root.TryGetProperty("Data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("Name", out _) &&
                        item.TryGetProperty("Id", out _) &&
                        item.TryGetProperty("Version", out _) &&
                        item.TryGetProperty("AvailableVersion", out _))
                    {
                        var currentVersion = item.GetProperty("Version").GetString() ?? "Unknown";
                        var availableVersion = item.GetProperty("AvailableVersion").GetString() ?? "Unknown";
                        var id = item.GetProperty("Id").GetString() ?? "";
                        var name = item.GetProperty("Name").GetString() ?? id;

                        if (!ShouldIncludePackage(id, currentVersion, availableVersion, includePinned, includeUnknown))
                            continue;

                        _availableUpdates.Add(new UpdateInfo(
                            name, id, currentVersion, availableVersion
                        ));
                    }
                }
            }

            _updateCount = _availableUpdates.Count;
            _logger.LogInformation("Parsed winget JSON output, found {UpdateCount} updates", _updateCount);
            if (_configManager != null)
            {
                _configManager.SetLastCheck(DateTime.Now);
            }
            _lastCheckTime = DateTime.Now;

            return _updateCount;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Failed to parse JSON: {Exception}", ex);
            return 0;
        }
    }

    private void ParsePackagesFromJson(JsonElement packages, bool includePinned, bool includeUnknown)
    {
        if (packages.ValueKind != JsonValueKind.Object)
            return;

        foreach (var pkg in packages.EnumerateObject())
        {
            var pkgId = pkg.Name;
            var pkgInfo = pkg.Value;

            var currentVersion = pkgInfo.TryGetProperty("Version", out var v) ? v.GetString() ?? "Unknown" : "Unknown";
            var availableVersion = pkgInfo.TryGetProperty("AvailableVersion", out var av) ? av.GetString() ?? "Unknown" : "Unknown";
            var name = pkgInfo.TryGetProperty("Name", out var n) ? n.GetString() ?? pkgId : pkgId;

            if (!ShouldIncludePackage(pkgId, currentVersion, availableVersion, includePinned, includeUnknown))
                continue;

            _availableUpdates.Add(new UpdateInfo(name, pkgId, currentVersion, availableVersion));
        }
    }

    private void ParseWingetOutput(ReadOnlySpan<char> output, bool includePinned, bool includeUnknown)
    {
        RefreshPinnedPackages(); // Sync version for text parsing

        var outputString = output.ToString();
        if (outputString.Contains("No updates found.") || outputString.Contains("No available upgrades."))
        {
            _logger.LogInformation("No updates available according to winget");
            _updateCount = 0;
            return;
        }

        var lines = outputString.Split('\n');
        _logger.LogDebug("Total lines in output: {LineCount}", lines.Length);
        for (int i = 0; i < Math.Min(25, lines.Length); i++)
        {
            _logger.LogDebug("Line {LineNum}: '{Content}'", i, lines[i]);
        }

        var sectionMarkers = new[]
        {
            "The following packages have an upgrade available, but require explicit targeting for upgrade:",
            "have version numbers that cannot be determined",
            "have pins that prevent upgrade"
        };

        var sections = SplitOutputIntoSections(lines, sectionMarkers);
        _logger.LogDebug("Found {SectionCount} sections", sections.Count);

        foreach (var section in sections)
        {
            _logger.LogDebug("Processing section with {LineCount} lines", section.Length);
            ProcessOutputSection(section, includePinned, includeUnknown);
        }

        _updateCount = _availableUpdates.Count;
        _logger.LogInformation("Parsed winget text output, found {UpdateCount} updates", _updateCount);
    }

    private bool ShouldIncludePackage(string id, string currentVersion, string availableVersion,
        bool includePinned, bool includeUnknown)
    {
        if ((currentVersion == "Unknown" || string.IsNullOrEmpty(currentVersion)) && !includeUnknown)
            return false;

        if (!IsValidVersionComparison(currentVersion, availableVersion))
            return false;

        if (!includePinned && IsPackagePinned(id))
            return false;

        return currentVersion != availableVersion;
    }

    private bool IsValidVersionComparison(string currentVersion, string availableVersion)
    {
        if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(availableVersion))
            return false;

        if (currentVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            availableVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return false;

        return currentVersion.Any(char.IsDigit) && availableVersion.Any(char.IsDigit);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> UpdatePackagesAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default)
    {
        var failed = new List<string>();

        foreach (var id in packageIds)
        {
            if (string.IsNullOrEmpty(id)) continue;

            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = $"upgrade --id \"{id}\" --accept-source-agreements --disable-interactivity",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = new Process { StartInfo = psi };
                process.Start();
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                if (process.ExitCode == 0)
                    _logger.LogInformation("Upgraded {Id}", id);
                else
                {
                    _logger.LogWarning("winget upgrade failed for {Id} (exit {Code})", id, process.ExitCode);
                    failed.Add(id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error upgrading {Id}", id);
                failed.Add(id);
            }
        }

        await CheckUpdatesAsync(force: true, cancellationToken: cancellationToken).ConfigureAwait(false);
        return failed.AsReadOnly();
    }

    /// <inheritdoc/>
    public async Task TogglePinAsync(IEnumerable<string> packageIds, CancellationToken cancellationToken = default)
    {
        foreach (var id in packageIds)
        {
            if (string.IsNullOrEmpty(id)) continue;

            var isPinned = IsPackagePinned(id);
            var arguments = isPinned ? $"pin remove --id \"{id}\"" : $"pin add --id \"{id}\"";

            var psi = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                if (isPinned) _pinnedPackages.Remove(id); else _pinnedPackages.Add(id);
                _logger.LogInformation("{Action} pin for {Id}", isPinned ? "Removed" : "Added", id);
            }
            else
            {
                _logger.LogWarning("Failed to {Action} pin for {Id}", isPinned ? "remove" : "add", id);
            }
        }

        // Sync IsPinned flags on cached updates
        for (int i = 0; i < _availableUpdates.Count; i++)
        {
            var u = _availableUpdates[i];
            var pinned = IsPackagePinned(u.Id);
            if (u.IsPinned != pinned)
                _availableUpdates[i] = u with { IsPinned = pinned };
        }
    }

    private async Task RefreshPinnedPackagesAsync(CancellationToken cancellationToken = default)
    {
        _pinnedPackages.Clear();

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "pin list",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Contains("---") || line.Contains("Name"))
                        continue;

                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        _pinnedPackages.Add(parts[1]);
                    }
                }

                _logger.LogInformation("Found {Count} pinned packages", _pinnedPackages.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to get pinned packages: {Exception}", ex);
        }
    }

    private void RefreshPinnedPackages()
    {
        RefreshPinnedPackagesAsync().GetAwaiter().GetResult();
    }

    private bool IsPackagePinned(string packageId)
    {
        return _pinnedPackages.Any(p =>
            packageId == p || p.StartsWith(packageId) || packageId.StartsWith(p)
        );
    }

    private static List<string[]> SplitOutputIntoSections(string[] lines, string[] sectionMarkers)
    {
        var sections = new List<string[]>();
        var currentSection = new List<string>();

        foreach (var line in lines)
        {
            // Check if this is a section marker (explicit targeting, unknown versions, etc.)
            var isMarker = sectionMarkers.Any(marker => line.Contains(marker));

            if (isMarker)
            {
                // Save previous section if it has content
                if (currentSection.Count > 0)
                {
                    sections.Add(currentSection.ToArray());
                }
                // Start new section with the marker line
                currentSection = new List<string> { line };
            }
            else if (IsHeaderLine(line))
            {
                // Found the header line - start first section
                if (currentSection.Count > 0)
                {
                    sections.Add(currentSection.ToArray());
                }
                currentSection = new List<string> { line };
            }
            else
            {
                currentSection.Add(line);
            }
        }

        // Add the last section
        if (currentSection.Count > 0)
        {
            sections.Add(currentSection.ToArray());
        }

        return sections;
    }

    private static bool IsHeaderLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith("Name") && trimmed.Contains("Id") && trimmed.Contains("Version");
    }

    // Winget guarantees 2+ spaces between columns; package names only contain single spaces.
    private static readonly System.Text.RegularExpressions.Regex ColumnSplitter =
        new(@"\s{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    private void ProcessOutputSection(string[] lines, bool includePinned, bool includeUnknown)
    {
        foreach (var line in lines)
        {
            if (ShouldSkipLine(line))
                continue;

            try
            {
                var parts = ColumnSplitter.Split(line.Trim());
                if (parts.Length < 4)
                    continue;

                var name = parts[0];
                var idStr = parts[1];
                var version = parts[2];
                var available = parts[3];

                _logger.LogDebug("Parsed: Name='{Name}', Id='{Id}', Version='{Version}', Available='{Available}'",
                    name, idStr, version, available);

                // Handle truncated IDs ending with '.'
                if (idStr.EndsWith('.'))
                {
                    foreach (var pinnedId in _pinnedPackages)
                    {
                        if (pinnedId.StartsWith(idStr))
                        {
                            idStr = pinnedId;
                            break;
                        }
                    }
                }

                name = CleanName(name);

                if (!ShouldIncludePackage(idStr, version, available, includePinned, includeUnknown))
                    continue;

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(idStr) &&
                    !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(available) &&
                    version != available)
                {
                    _availableUpdates.Add(new UpdateInfo(name, idStr, version, available));
                    _logger.LogInformation("Added update: {Name} ({Id}) {Version} -> {Available}", name, idStr, version, available);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error parsing line: {Line}: {Exception}", line, ex);
            }
        }
    }

    private static bool ShouldSkipLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return true;
        var trimmed = line.Trim();
        if (trimmed.Length > 0 && trimmed.All(c => c == '-'))
            return true;
        if (IsHeaderLine(line))
            return true;
        var lower = line.ToLowerInvariant();
        return lower.Contains("upgrades available") ||
               lower.Contains("no updates found") ||
               lower.Contains("prevent upgrade") ||
               lower.Contains("explicit targeting");
    }

    private static string ExtractVersion(string input)
    {
        var match = System.Text.RegularExpressions.Regex.Match(input, @"\d+(\.\d+)+");
        return match.Success ? match.Value : input.Trim();
    }

    private static string CleanName(string name)
    {
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\([^)]*\)", "");
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+\d+(\.\d+)+$", "");
        return name.Trim();
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _availableUpdates.Clear();
        _pinnedPackages.Clear();
        return ValueTask.CompletedTask;
    }

}
