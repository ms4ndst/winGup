using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinGup.Models;

/// <summary>
/// Represents a message in the IPC protocol between service and UI.
/// </summary>
/// <param name="Command">The command name (e.g., "check_updates", "get_status")</param>
/// <param name="Data">Optional JSON-serialized data payload</param>
/// <param name="Timestamp">ISO 8601 timestamp when the message was created</param>
public record class IpcMessage(
    string Command,
    string? Data = null,
    string? Timestamp = null
)
{
    /// <summary>
    /// Serializes the message to a JSON string.
    /// </summary>
    /// <returns>JSON string representation of the message</returns>
    public string ToJson()
    {
        var timestamp = string.IsNullOrEmpty(Timestamp) ? DateTime.UtcNow.ToString("o") : Timestamp;
        var message = new IpcMessage(Command, Data, timestamp);
        return JsonSerializer.Serialize(message);
    }

    /// <summary>
    /// Deserializes a message from a JSON string.
    /// </summary>
    /// <param name="json">JSON string to deserialize</param>
    /// <returns>Deserialized IpcMessage, or null if parsing fails</returns>
    public static IpcMessage? FromJson(ReadOnlySpan<char> json)
    {
        try
        {
            return JsonSerializer.Deserialize<IpcMessage>(json.ToString());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
