using System.Buffers;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Client for communicating with the Winget Updater service via named pipes.
/// </summary>
public partial class IpcClient : IIpcClient, IDisposable
{
    private const string PipeName = @"\\.\pipe\WingetUpdaterPipe";
    private const int BufferSize = 4096;
    private const int DefaultTimeoutSeconds = 10;

    private readonly ILogger<IpcClient> _logger;
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcClient"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public IpcClient(ILogger<IpcClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task ConnectAsync()
    {
        await Task.Run(() => Connect()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> SendMessageAsync(string command, string? data = null)
    {
        return await Task.Run(() =>
        {
            var result = SendCommand(command, data);
            return result?.Data;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects to the IPC server with a timeout.
    /// </summary>
    /// <param name="timeoutSeconds">Timeout in seconds (default: 10)</param>
    /// <returns>True if connected successfully, false otherwise</returns>
    public bool Connect(int timeoutSeconds = DefaultTimeoutSeconds)
    {
        var startTime = DateTime.Now;

        while ((DateTime.Now - startTime).TotalSeconds < timeoutSeconds)
        {
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".",
                    PipeName.TrimStart('\\', '.'),
                    PipeDirection.InOut,
                    PipeOptions.None
                );

                _pipe.Connect(TimeSpan.FromSeconds(1));
                _pipe.ReadMode = PipeTransmissionMode.Message;

                _logger.LogInformation("Connected to IPC server");
                return true;
            }
            catch (TimeoutException)
            {
                // Server isn't ready yet, retry
                _pipe?.Dispose();
                _pipe = null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error connecting to IPC server: {Exception}", ex);
                _pipe?.Dispose();
                _pipe = null;
                return false;
            }
        }

                _logger.LogError("Timeout connecting to IPC server after {TimeoutSeconds} seconds", timeoutSeconds);
        return false;
    }

    /// <summary>
    /// Disconnects from the IPC server.
    /// </summary>
    public void Disconnect()
    {
        if (_pipe == null)
            return;

        try
        {
            _pipe.Close();
                _logger.LogInformation("Disconnected from IPC server");
        }
        catch (Exception ex)
        {
                _logger.LogError("Error disconnecting from IPC server: {Exception}", ex);
        }
        finally
        {
            _pipe.Dispose();
            _pipe = null;
        }
    }

    /// <summary>
    /// Sends a command to the server and gets the response.
    /// </summary>
    /// <param name="command">The command name</param>
    /// <param name="data">Optional data payload (JSON string)</param>
    /// <returns>The response message, or null if communication failed</returns>
    public IpcMessage? SendCommand(string command, string? data = null)
    {
        if (_pipe == null)
        {
            if (!Connect())
                return null;
        }

        try
        {
            var message = new IpcMessage(command, data);
            var messageJson = message.ToJson();
            var messageBytes = Encoding.UTF8.GetBytes(messageJson);

            _pipe!.Write(messageBytes, 0, messageBytes.Length);

            // Read in a loop — named pipe message mode may deliver large responses in chunks
            using var ms = new System.IO.MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                do
                {
                    var bytesRead = _pipe.Read(buffer, 0, BufferSize);
                    if (bytesRead == 0) break;
                    ms.Write(buffer, 0, bytesRead);
                } while (!_pipe.IsMessageComplete);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (ms.Length == 0)
                return null;

            var responseJson = Encoding.UTF8.GetString(ms.ToArray());
            return IpcMessage.FromJson(responseJson.AsSpan());
        }
        catch (Exception ex)
        {
                _logger.LogError("Error in IPC communication: {Exception}", ex);
            Disconnect();
            return null;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Disconnect();
    }

}
