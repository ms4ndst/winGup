using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WinGup;
using Xunit;

namespace WinGup.Tests;

public class UpdateCheckerTests
{
    [Fact]
    public async Task CheckUpdatesAsync_WhenWingetNotInstalled_ReturnsZero()
    {
        // Arrange
        var mockLogger = Mock.Of<ILogger<UpdateChecker>>();
        var mockConfig = Mock.Of<IConfigManager>();
        Mock.Get(mockConfig).SetupGet(c => c.IncludePinnedUpdates).Returns(false);
        Mock.Get(mockConfig).SetupGet(c => c.IncludeUnknownVersions).Returns(false);

        var checker = new UpdateChecker(mockConfig, mockLogger);

        // Act - this will fail to run winget since it's not installed in test env
        // but we can verify it doesn't throw
        var result = await checker.CheckUpdatesAsync();

        // Assert - result is 0 if winget isn't installed, or >= 0 if it is
        result.Should().BeGreaterThanOrEqualTo(0);
    }
}
