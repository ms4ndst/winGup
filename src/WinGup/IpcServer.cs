using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WinGup.Models;

namespace WinGup;

/// <summary>
/// Server-side IPC handler using Named Pipes for communication between service and UI.
/// </summary>
/// <remarks>
/// Uses <see cref="NamedPipeServerStream"/> for message-based IPC communication.
/// </remarks>
public partial class IpcServer : IDisposable
{
    private const string PipeName = @"\\.\pipe\WingetUpdaterPipe";
    private const int BufferSize = 4096;

    private readonly ILogger<IpcServer> _logger;
    private readonly IUpdateChecker _updateChecker;
    private readonly Dictionary<string, Func<string?, string?>> _commandHandlers = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private Thread? _serverThread;
    private bool _running;
    private NamedPipeServerStream? _pipe;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcServer"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="updateChecker">Update checker for getting available updates</param>
    public IpcServer(ILogger<IpcServer> logger, IUpdateChecker updateChecker)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));

        // Register command handlers
        RegisterHandler("check_updates", _ => HandleCheckUpdates());
        RegisterHandler("get_status", _ => HandleGetStatus());
        RegisterHandler("get_updates", _ => HandleGetUpdates());
        RegisterHandler("get_last_check", _ => HandleGetLastCheck());
        RegisterHandler("save_settings", data => HandleSaveSettings(data));
        RegisterHandler("get_settings", _ => HandleGetSettings());
        RegisterHandler("toggle_pin", data => HandleTogglePin(data));
    }

    /// <summary>
    /// Registers a handler function for a specific command.
    /// </summary>
    /// <param name="command">The command name to register</param>
    /// <param name="handlerFunc">The handler function that processes the command</param>
    public void RegisterHandler(string command, Func<string?, string?> handlerFunc)
    {
        _commandHandlers[command] = handlerFunc;
        _logger.LogInformation("Handler registered for command: {Command}", command);
    }

    /// <summary>
    /// Starts the IPC server in a background thread.
    /// </summary>
    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _serverThread = new Thread(RunServer)
        {
            IsBackground = true,
            Name = "IPC Server Thread"
        };
        _serverThread.Start();
        _logger.LogInformation("IPC server started");
    }

    /// <summary>
    /// Stops the IPC server and releases resources.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _cancellationTokenSource.Cancel();

        try
        {
            _pipe?.Close();
        }
        catch
        {
            // Ignore errors during shutdown
        }

        _logger.LogInformation("IPC server stopped");
    }

    private void RunServer()
    {
        while (_running && !_cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeServerStream(
                    PipeName.TrimStart('\\', '.'),
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.None
                );

                _logger.LogDebug("Waiting for client connection...");
                _pipe.WaitForConnection();
                _logger.LogDebug("Client connected");

                while (_running && _pipe.IsConnected)
                {
                    try
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                        try
                        {
                            var memory = buffer.AsMemory(0, BufferSize);
                            var bytesRead = _pipe.Read(memory.Span);
                            if (bytesRead == 0)
                                break;

                            var messageJson = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            var message = IpcMessage.FromJson(messageJson.AsSpan());

                            if (message == null)
                                continue;

                            _logger.LogDebug("Received command: {Command}", message.Command);
                            var response = HandleCommand(message);
                            var responseJson = response.ToJson();
                            var responseBytes = Encoding.UTF8.GetBytes(responseJson);
                            _pipe.Write(responseBytes);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                    catch (IOException ex) when (IsPipeBroken(ex))
                    {
                        _logger.LogDebug("Client disconnected");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Error processing request: {Exception}", ex);

                        try
                        {
                            var errorResponse = new IpcMessage("error", JsonSerializer.Serialize(new { message = ex.Message }));
                            var errorBytes = Encoding.UTF8.GetBytes(errorResponse.ToJson());
                            _pipe.Write(errorBytes);
                        }
                        catch
                        {
                            // Ignore errors when sending error response
                        }
                        break;
                    }
                }

                try
                {
                    _pipe.Close();
                }
                catch
                {
                    // Ignore close errors
                }

                _pipe = null;
            }
            catch (Exception ex)
            {
                        _logger.LogError("Server error: {Exception}", ex);
                Thread.Sleep(1000); // Avoid rapid retries
            }
        }
    }

    private IpcMessage HandleCommand(IpcMessage message)
    {
        if (_commandHandlers.TryGetValue(message.Command, out var handler))
        {
            try
            {
                var result = handler(message.Data);
                return new IpcMessage("response", result);
            }
            catch (Exception ex)
            {
                        _logger.LogError("Error in command handler for {Command}: {Exception}", message.Command, ex);
                return new IpcMessage("error", JsonSerializer.Serialize(new { message = ex.Message }));
            }
        }

        return new IpcMessage("error", JsonSerializer.Serialize(new { message = $"Unknown command: {message.Command}" }));
    }

    private static bool IsPipeBroken(IOException ex)
    {
        return ex.HResult == unchecked((int)0x8007006D); // ERROR_BROKEN_PIPE
    }

    private string? HandleCheckUpdates()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _updateChecker.CheckUpdatesAsync(force: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking updates");
            }
        });
        return null;
    }

    private string? HandleGetStatus()
    {
        var status = new
        {
            update_count = _updateChecker.UpdateCount,
            last_check = _updateChecker.LastCheckTime
        };
        return System.Text.Json.JsonSerializer.Serialize(status);
    }

    private string? HandleGetUpdates()
    {
        var updates = _updateChecker.GetCachedUpdates();
        return System.Text.Json.JsonSerializer.Serialize(updates);
    }

    private string? HandleGetLastCheck()
    {
        var lastCheck = _updateChecker.LastCheckTime;
        return lastCheck?.ToString("o") ?? "";
    }

    private string? HandleSaveSettings(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        // Parse and save settings
        // Simplified - full implementation would parse JSON and update ConfigManager
        return "Settings saved";
    }

    private string? HandleGetSettings()
    {
        // Return current settings as JSON
        // Simplified - full implementation would return config
        return "{}";
    }

    private string? HandleTogglePin(string? data)
    {
        if (string.IsNullOrEmpty(data))
            return null;

        // Toggle pin for selected package IDs
        // Simplified - full implementation would update pinned packages
        return "Pin toggled";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _cancellationTokenSource.Dispose();
        _pipe?.Dispose();
    }

}
