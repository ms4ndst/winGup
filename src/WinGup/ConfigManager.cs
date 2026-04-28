using System.Globalization;
using Microsoft.Extensions.Logging;

namespace WinGup;

/// <summary>
/// Manages the INI configuration settings for the Winget Updater application.
/// </summary>
/// <remarks>
/// Configuration is stored in %LOCALAPPDATA%\WingetUpdater\settings.ini.
/// </remarks>
public partial class ConfigManager : IConfigManager, IDisposable
{
    private const string ConfigSection = "Settings";
    private const string DefaultMorningCheck = "08:00";
    private const string DefaultAfternoonCheck = "16:00";
    private const string DefaultLastCheck = "";

    private readonly string _configFilePath;
    private readonly ILogger<ConfigManager> _logger;
    private readonly Dictionary<string, string> _settings = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigManager"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="configFile">Optional config file name (default: settings.ini)</param>
    public ConfigManager(ILogger<ConfigManager> logger, string configFile = "settings.ini")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configDir = Path.Combine(localAppData, "WingetUpdater");

        if (!Directory.Exists(configDir))
        {
            try
            {
                Directory.CreateDirectory(configDir);
                _logger.LogInformation("Created configuration directory: {ConfigDir}", configDir);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to create configuration directory: {Exception}", ex);
                configDir = AppContext.BaseDirectory;
            }
        }

        _configFilePath = Path.Combine(configDir, configFile);
        LoadDefaults();
        Load();
    }

    /// <inheritdoc/>
    public string MorningCheckTime
    {
        get => GetSetting("morning_check", DefaultMorningCheck);
        set
        {
            SetSetting("morning_check", value);
            Save();
        }
    }

    /// <inheritdoc/>
    public string AfternoonCheckTime
    {
        get => GetSetting("afternoon_check", DefaultAfternoonCheck);
        set
        {
            SetSetting("afternoon_check", value);
            Save();
        }
    }

    /// <inheritdoc/>
    public bool NotifyOnUpdates
    {
        get => GetSetting("notify_on_updates", "True").Equals("True", StringComparison.OrdinalIgnoreCase);
        set
        {
            SetSetting("notify_on_updates", value.ToString());
            Save();
        }
    }

    /// <inheritdoc/>
    public bool AutoCheck
    {
        get => GetSetting("auto_check", "True").Equals("True", StringComparison.OrdinalIgnoreCase);
        set
        {
            SetSetting("auto_check", value.ToString());
            Save();
        }
    }

    /// <inheritdoc/>
    public bool IncludePinnedUpdates
    {
        get => GetSetting("include_pinned_updates", "False").Equals("True", StringComparison.OrdinalIgnoreCase);
        set
        {
            SetSetting("include_pinned_updates", value.ToString());
            Save();
        }
    }

    /// <inheritdoc/>
    public bool IncludeUnknownVersions
    {
        get => GetSetting("include_unknown_versions", "False").Equals("True", StringComparison.OrdinalIgnoreCase);
        set
        {
            SetSetting("include_unknown_versions", value.ToString());
            Save();
        }
    }

    /// <inheritdoc/>
    public DateTime? LastCheck
    {
        get
        {
            var value = GetSetting("last_check", DefaultLastCheck);
            if (string.IsNullOrEmpty(value))
                return null;

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result))
                return result;

            return null;
        }
        set
        {
            SetSetting("last_check", value?.ToString("o") ?? "");
            Save();
        }
    }

    /// <inheritdoc/>
    public void Save()
    {
        try
        {
            using var writer = new StreamWriter(_configFilePath);
            writer.WriteLine($"[{ConfigSection}]");
            foreach (var kvp in _settings)
            {
                writer.WriteLine($"{kvp.Key} = {kvp.Value}");
            }
                _logger.LogInformation("Configuration saved to {FilePath}", _configFilePath);
        }
        catch (Exception ex)
        {
                _logger.LogError("Failed to save configuration: {Exception}", ex);
        }
    }

    /// <inheritdoc/>
    public void Load()
    {
        if (!File.Exists(_configFilePath))
        {
                _logger.LogWarning("Configuration file not found: {FilePath}", _configFilePath);
            return;
        }

        try
        {
            var lines = File.ReadAllLines(_configFilePath);
            var inSettingsSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inSettingsSection = trimmed.Equals($"[{ConfigSection}]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSettingsSection)
                    continue;

                var equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex < 0)
                    continue;

                var key = trimmed[..equalsIndex].Trim();
                var value = trimmed[(equalsIndex + 1)..].Trim();
                _settings[key] = value;
            }

                _logger.LogInformation("Loaded configuration from {FilePath}", _configFilePath);
        }
        catch (Exception ex)
        {
                _logger.LogError("Failed to load configuration: {Exception}", ex);
        }
    }

    private void LoadDefaults()
    {
        _settings["morning_check"] = DefaultMorningCheck;
        _settings["afternoon_check"] = DefaultAfternoonCheck;
        _settings["notify_on_updates"] = "True";
        _settings["last_check"] = DefaultLastCheck;
        _settings["auto_check"] = "True";
        _settings["include_pinned_updates"] = "False";
        _settings["include_unknown_versions"] = "False";
    }

    private string GetSetting(string key, string defaultValue)
    {
        return _settings.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private void SetSetting(string key, string value)
    {
        _settings[key] = value;
    }

    /// <inheritdoc/>
    public void SetLastCheck()
    {
        SetLastCheck(DateTime.Now);
    }

    /// <inheritdoc/>
    public void SetLastCheck(DateTime value)
    {
        LastCheck = value;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _settings.Clear();
    }
}
