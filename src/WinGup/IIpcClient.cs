using System.Threading.Tasks;

namespace WinGup;

/// <summary>
/// Interface for IPC client communication with the service
/// Ported from Python ipc_handler.py client patterns
/// </summary>
public interface IIpcClient
{
    /// <summary>
    /// Sends a command to the service and returns the response
    /// </summary>
    Task<string?> SendMessageAsync(string command, string? data = null);

    /// <summary>
    /// Connects to the named pipe server
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// Disconnects from the named pipe server
    /// </summary>
    void Disconnect();
}
