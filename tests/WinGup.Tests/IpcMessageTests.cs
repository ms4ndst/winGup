using System;
using FluentAssertions;
using WinGup.Models;
using Xunit;

namespace WinGup.Tests;

public class IpcMessageTests
{
    [Fact]
    public void ToJson_SerializesCorrectly()
    {
        // Arrange
        var message = new IpcMessage("test_command", "{\"key\":\"value\"}", "2026-04-27T12:00:00Z");

        // Act
        var json = message.ToJson();

        // Assert
        json.Should().Contain("test_command");
        json.Should().Contain("key");
    }

    [Fact]
    public void FromJson_DeserializesCorrectly()
    {
        // Arrange
        var json = "{\"Command\":\"test\",\"Data\":\"data\",\"Timestamp\":\"2026-04-27T12:00:00Z\"}";

        // Act
        var message = IpcMessage.FromJson(json.AsSpan());

        // Assert
        message.Should().NotBeNull();
        message!.Command.Should().Be("test");
        message.Data.Should().Be("data");
    }
}
