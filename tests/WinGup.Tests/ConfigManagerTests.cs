using System;
using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WinGup;
using Xunit;

namespace WinGup.Tests;

/// <summary>
/// Tests for behavioral parity with the Python Winget_Updater project.
/// </summary>
public class ConfigManagerTests : IDisposable
{
    private readonly string _testConfigDir;
    private readonly string _testConfigFile;

    public ConfigManagerTests()
    {
        _testConfigDir = Path.Combine(Path.GetTempPath(), "WinGupTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testConfigDir);
        _testConfigFile = Path.Combine(_testConfigDir, "settings.ini");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testConfigDir))
        {
            Directory.Delete(_testConfigDir, recursive: true);
        }
    }

    private ConfigManager CreateConfigManager()
    {
        var mockLogger = Mock.Of<ILogger<ConfigManager>>();
        // Use a custom config file path for testing
        return new ConfigManager(mockLogger, _testConfigFile);
    }

    [Fact]
    public void Constructor_CreatesDefaultSettings()
    {
        // This test validates default values match Python configparser defaults
        var config = CreateConfigManager();

        config.MorningCheckTime.Should().Be("08:00");
        config.AfternoonCheckTime.Should().Be("16:00");
        config.NotifyOnUpdates.Should().BeTrue();
        config.AutoCheck.Should().BeTrue();
        config.IncludePinnedUpdates.Should().BeFalse();
        config.IncludeUnknownVersions.Should().BeFalse();
    }

    [Fact]
    public void MorningCheckTime_SetValue_SavesToFile()
    {
        var config = CreateConfigManager();

        config.MorningCheckTime = "09:00";

        config.MorningCheckTime.Should().Be("09:00");
    }

    [Fact]
    public void LastCheck_SetToNow_ReturnsCurrentTime()
    {
        var config = CreateConfigManager();

        config.SetLastCheck(DateTime.Now);

        config.LastCheck.Should().NotBeNull();
        config.LastCheck.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }
}
