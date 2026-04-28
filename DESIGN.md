# Phase 2: Design of C# Port (winGup)

## 1. Project Layout

### 1.1 Solution and Project Structure

```
winGup/
├── WinGup.sln                      # Solution file
├── global.json                     # .NET SDK version pinning (8.0 LTS)
├── Directory.Build.props           # Shared build properties
├── .editorconfig                   # Code style rules
│
├── src/
│   └── WinGup/                    # Main project (namespace: WinGup)
│       ├── WinGup.csproj
│       ├── Program.cs              # Entry point (CLI argument parsing)
│       ├── WingetUpdaterService.cs # Windows Service implementation
│       ├── UpdateChecker.cs        # Winget subprocess execution + parsing
│       ├── ConfigManager.cs        # INI configuration management
│       ├── IpcServer.cs            # Named Pipe IPC server
│       ├── IpcClient.cs            # Named Pipe IPC client
│       ├── TrayApplication.cs      # System tray application (primary UI)
│       ├── SettingsWindow.cs       # Settings window (using modern UI framework)
│       ├── UpdateListWindow.cs     # Update list window
│       ├── WindowManager.cs        # Singleton window manager
│       ├── Models/
│       │   ├── UpdateInfo.cs       # Update data record
│       │   ├── IpcMessage.cs       # IPC message record
│       │   └── ServiceStatus.cs    # Service status record
│       ├── Parsers/
│       │   ├── WingetJsonParser.cs
│       │   └── WingetTextParser.cs
│       └── Resources/
│           └── winget_updater.ico
│
├── tests/
│   └── WinGup.Tests/
│       ├── WinGup.Tests.csproj
│       ├── UpdateCheckerTests.cs
│       ├── ConfigManagerTests.cs
│       ├── IpcTests.cs
│       └── ParserTests.cs
│
├── docs/
│   ├── architecture.md
│   ├── memory-model.md
│   ├── api-reference.md
│   └── adr/
│       ├── 0001-use-named-pipes-for-ipc.md
│       ├── 0002-use-system-text-json.md
│       ├── 0003-use-uraniumui-or-avalonia-for-ui.md
│       └── 0004-use-microsoft-extensions-logging.md
│
├── README.md
├── CHANGELOG.md
└── ANALYSIS.md                    # From Phase 1
```

### 1.2 Naming Conventions

| Convention | Rule | Example |
|------------|------|---------|
| **Namespace** | `WinGup` (top-level), `WinGup.Models`, `WinGup.Parsers` | `WinGup.UpdateChecker` |
| **Classes** | PascalCase, noun | `UpdateChecker`, `ConfigManager`, `IpcServer` |
| **Interfaces** | PascalCase with `I` prefix | `IUpdateChecker`, `IConfigManager` |
| **Records** | PascalCase, immutable data | `UpdateInfo`, `IpcMessage` |
| **Methods** | PascalCase, verb-noun | `CheckUpdatesAsync()`, `GetUpdateCount()` |
| **Async methods** | Suffix `Async` | `CheckUpdatesAsync()`, `InstallAllUpdatesAsync()` |
| **Parameters** | camelCase | `includePinned`, `cancellationToken` |
| **Local variables** | camelCase | `updateCount`, `pinnedPackages` |
| **Constants** | PascalCase or UPPER_SNAKE | `PIPE_NAME`, `BUFFER_SIZE` |
| **Files** | One type per file, matching type name | `UpdateChecker.cs`, `IpcServer.cs` |

---

## 2. Type Design

### 2.1 Immutable Data Records

**`UpdateInfo`** — Represents a single available package update.
```csharp
/// <summary>
/// Represents a Winget package update with current and available versions.
/// </summary>
/// <param name="Name">Display name of the package</param>
/// <param name="Id">Winget package identifier</param>
/// <param name="CurrentVersion">Currently installed version</param>
/// <param name="AvailableVersion">Version available for upgrade</param>
/// <param name="Source">Source repository (e.g., "winget")</param>
public readonly record struct UpdateInfo(
    string Name,
    string Id,
    string CurrentVersion,
    string AvailableVersion,
    string Source
);
```
**Rationale**: `readonly record struct` — small value-type, immutable, no heap allocation per update in hot paths. Use `in` parameter passing in hot loops.

**`IpcMessage`** — IPC message envelope.
```csharp
/// <summary>
/// Represents a message in the IPC protocol between service and UI.
/// </summary>
/// <param name="Command">The command name (e.g., "check_updates", "get_status")</param>
/// <param name="Data">Optional data payload (JSON serialized)</param>
/// <param name="Timestamp">ISO 8601 timestamp when the message was created</param>
public record class IpcMessage(
    string Command,
    string? Data = null,
    string? Timestamp = null
);
```
**Rationale**: `record class` — reference type with value-based equality, mutable Data for JSON deserialization. Timestamp defaults to `DateTime.UtcNow`.

**`ServiceStatus`** — Status information returned by the service.
```csharp
/// <summary>
/// Represents the current status of the Winget Updater service.
/// </summary>
public record class ServiceStatus(
    int UpdateCount,
    DateTime? LastCheck,
    bool AutoCheck,
    string MorningCheck,
    string AfternoonCheck
);
```

### 2.2 Primary Classes

**`UpdateChecker`** — Core winget interaction logic.
- **Why class, not struct**: Holds mutable state (`availableUpdates` list, `isChecking` flag, `pinnedPackages` cache), implements `IAsyncDisposable` for subprocess handles.
- **Key design**: Use `List<UpdateInfo>` reused via `.Clear()` to avoid allocations; `HashSet<string>` for pinned packages.

**`ConfigManager`** — INI configuration management.
- **Why class**: Holds file path state, uses `ILogger<ConfigManager>`.
- **INI parsing**: Use `IniFile` class (custom implementation or `Microsoft.Extensions.Configuration.Ini`), writing via `StreamWriter`.

**`IpcServer`** — Named Pipe server for service→UI communication.
- **Why class**: Manages thread lifecycle, registers command handlers (`Dictionary<string, Func<string, string>>`).
- **.NET API**: `NamedPipeServerStream` with `PipeDirection.InOut`, `PipeTransmissionMode.Message`.

**`IpcClient`** — Named Pipe client for UI→service communication.
- **Why class**: Holds pipe connection state, retry logic.
- **.NET API**: `NamedPipeClientStream`.

**`WingetUpdaterService`** — Windows Service implementation.
- **Why class**: Inherits from `BackgroundService` (.NET Generic Host) or implements `IHostLifetime`.
- **.NET 8 approach**: Use `IHost` with `WindowsServiceLifetime`, which is the modern way to build Windows Services in .NET.

**`TrayApplication`** — System tray application.
- **UI Framework Decision**: See ADR-0003. Using **Avalonia** for cross-platform capability with native Windows tray support, or **Windows Forms** for simplicity since this is Windows-only.
- **Decision**: Use **Windows Forms** (`System.Windows.Forms`) for the tray icon since the app is Windows-only and WinForms has good system tray support via `NotifyIcon`.

**`WindowManager`** — Singleton Tkinter window manager equivalent.
- **Why class + singleton**: Manages single `Application` instance, window lifecycle.
- **.NET**: Use `Application.Run()` message loop, `SynchronizationContext` for thread-safe window creation.

### 2.3 Interfaces (for testability)

```csharp
public interface IUpdateChecker
{
    bool IsChecking { get; }
    int UpdateCount { get; }
    IReadOnlyList<UpdateInfo> AvailableUpdates { get; }
    Task<int> CheckUpdatesAsync(bool force = false, bool includePinned = false, bool includeUnknown = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UpdateInfo>> GetUpdatesListAsync(bool includePinned = false, bool includeUnknown = false, CancellationToken cancellationToken = default);
    Task<int> GetUpdateCountAsync(bool includePinned = false, bool includeUnknown = false, CancellationToken cancellationToken = default);
    Task<bool> InstallAllUpdatesAsync(CancellationToken cancellationToken = default);
}

public interface IConfigManager
{
    string MorningCheckTime { get; set; }
    string AfternoonCheckTime { get; set; }
    bool NotifyOnUpdates { get; set; }
    bool AutoCheck { get; set; }
    bool IncludePinnedUpdates { get; set; }
    bool IncludeUnknownVersions { get; set; }
    DateTime? LastCheck { get; set; }
    void Save();
    void Load();
}

public interface ILogger<T> where T : class
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(string message, params object[] args);
    void LogDebug(string message, params object[] args);
}
```

---

## 3. Mapping Table

### 3.1 Python → C# Language Construct Mapping

| Python Construct | C# Equivalent | Notes |
|------------------|---------------|-------|
| `class WingetUpdateChecker:` | `class UpdateChecker` | Rename to follow C# naming (no "Winget" prefix redundancy) |
| `def __init__(self, ...):` | Constructor `UpdateChecker(IConfigManager, ILogger<UpdateChecker>)` | Use DI for dependencies |
| `self.available_updates = []` | `private readonly List<UpdateInfo> _availableUpdates = new();` | Reuse list, call `.Clear()` |
| `self.pinned_packages = set()` | `private readonly HashSet<string> _pinnedPackages = new();` | Use `.Clear()` to reuse |
| `async def check_updates_async(self):` | `public async Task<int> CheckUpdatesAsync(...)` | C# async/await, CancellationToken |
| `subprocess.run([...], capture_output=True)` | `Process.Start()` with `RedirectStandardOutput/Error` | Use `TaskCompletionSource` for async |
| `json.loads(output)` | `JsonDocument.Parse(utf8Bytes)` or `Utf8JsonReader` | Prefer `Utf8JsonReader` for zero-alloc |
| `re.split(r'\s{2,}', line)` | `line.Split("  ", StringSplitOptions.None)` + `ReadOnlySpan<char>` | Avoid regex for simple splits |
| `configparser.ConfigParser` | Custom `IniFile` class or `Microsoft.Extensions.Configuration` | Custom INI parser for full compatibility |
| `win32pipe.CreateNamedPipe()` | `new NamedPipeServerStream(...)` | `System.IO.Pipes` namespace |
| `win32file.ReadFile()` | `pipe.ReadAsync(buffer, cancellationToken)` | Returns `int` bytes read |
| `win32file.WriteFile()` | `pipe.WriteAsync(buffer, cancellationToken)` | |
| `threading.Thread(target=...)` | `Task.Run()` or `new Thread(...)` | Prefer `Task` for thread pool work |
| `pystray.Icon` | `NotifyIcon` (WinForms) | System tray icon |
| `tkinter.Tk()` | `new Form()` or `Application.Run()` | WinForms application loop |
| `tkinter.ttk.Treeview` | `DataGridView` or `ListView` (WinForms) | Update list display |
| `logging.getLogger()` | `ILogger<T>` via DI | Microsoft.Extensions.Logging |
| `argparse.ArgumentParser` | `System.CommandLine` or simple `args` parsing | Use `System.CommandLine` for rich CLI |
| `schedule` library | `System.Threading.Timer` or manual loop | .NET built-in, no external dep |
| `time.sleep(seconds)` | `await Task.Delay(milliseconds, cancellationToken)` | Cancellable delay |

### 3.2 Module Mapping

| Python File | C# File(s) | Notes |
|-------------|------------|-------|
| `launcher.py` | `Program.cs` + `CommandLineParser.cs` | Entry point, CLI argument handling |
| `service_component.py` | `WingetUpdaterService.cs` + `ServiceWorker.cs` | BackgroundService for .NET 8 |
| `update_checker.py` | `UpdateChecker.cs` + `Parsers/WingetJsonParser.cs` + `Parsers/WingetTextParser.cs` | Split parsing into separate classes |
| `ipc_handler.py` | `IpcServer.cs` + `IpcClient.cs` + `Models/IpcMessage.cs` | Records for messages |
| `config_manager.py` | `ConfigManager.cs` + `Models/AppSettings.cs` | INI parsing |
| `ui_component.py` | `TrayApplication.cs` + `SettingsWindow.cs` + `UpdateListWindow.cs` + `WindowManager.cs` | WinForms UI |
| `system_tray.py` | *Not ported* | Legacy, superseded by `ui_component.py` |
| `window_manager.py` | `WindowManager.cs` | Singleton pattern, thread-safe |
| `build_installer.py` | *Not ported* | Use `dotnet publish` + Inno Setup separately |
| `create_icon.py` | `IconGenerator.cs` | Use `System.Drawing` or `ImageSharp` |

---

## 4. Memory Strategy

> **This is a hard requirement of the task.**

### 4.1 General Principles

| Rule | Implementation |
|------|-----------------|
| Prefer `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>` over arrays | Use `ReadOnlySpan<char>` for parsing winget output lines |
| Use `ArrayPool<T>.Shared` for transient buffers > 1 KiB | For IPC buffers (4 KiB), use `ArrayPool<byte>.Shared.Rent(4096)` |
| Use `string` interning / `ReadOnlySpan<char>` parsing | Parse winget text output with `Span` slicing, not `string.Split` |
| Prefer `IAsyncEnumerable<T>` and streaming | Stream winget output line-by-line instead of materializing whole string |
| Avoid LINQ in hot paths | Use `for` / `foreach` over `List<UpdateInfo>` for update checking |
| Mark structs `readonly` where possible | `UpdateInfo` is `readonly record struct` |
| Pass large structs by `in` | `in ReadOnlySpan<char>` in parsing methods |
| Use `StringBuilder` only when concatenation count > ~4 | Only in IPC JSON serialization if needed |
| Set `<ServerGarbageCollection>` and `<ConcurrentGarbageCollection>` | See `WinGup.csproj` settings below |
| Justify each allocation in per-request/per-iteration paths | Documented below |

### 4.2 Project File GC Settings (`WinGup.csproj`)

```xml
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
  <ConcurrentGarbageCollection>true</ConcurrentGarbageCollection>
</PropertyGroup>
```

**Rationale**: This is a long-running service process. Server GC provides better throughput for the sustained workload. Concurrent GC allows UI threads to remain responsive during collections.

### 4.3 Hot Path Allocation Analysis

#### Hot Path 1: `CheckUpdatesAsync()` — Winget Output Parsing

| Step | Python (allocation) | C# (zero/minimal alloc) |
|------|---------------------|--------------------------|
| Read winget output | `stdout.decode('utf-8')` → `string` | `Process.StandardOutput.BaseStream` → read into `Span<byte>` or `PipeReader` |
| Split into lines | `output.strip().split('\n')` → `List<string>` (N allocations) | `ReadOnlySpan<byte>` with `MemoryExtensions.IndexOf(..., '\n')` to slice without allocating strings |
| Parse each line | `re.split(r'\s{2,}', line)` → `string[]` (per line) | `ReadOnlySpan<char>` with `MemoryExtensions.Split(..., "  ")` or manual index scanning |
| Create UpdateInfo | `{'name': ..., 'id': ...}` → `dict` (heap) | `new UpdateInfo(name, id, ...)` as `readonly record struct` (stack or inline in List) |
| Store in list | `self.available_updates.append(...)` → `list` grows | `List<UpdateInfo>.Add(...)` — `UpdateInfo` is struct, stored inline in List's backing array |

**Justification for allocations in this path:**
- `ProcessStartInfo` and `Process` objects: 1 per check (acceptable, not per-update)
- `List<UpdateInfo>` backing array: Grows as needed, structs stored inline (no per-item heap alloc)
- String for winget output: Unavoidable since `Process` returns string; use `Span` for parsing to avoid further allocs

#### Hot Path 2: IPC Message Serialization

| Step | Python (allocation) | C# (zero/minimal alloc) |
|------|---------------------|--------------------------|
| Serialize to JSON | `json.dumps(...)` → `string` | `JsonSerializer.SerializeToUtf8Bytes(...)` → `byte[]` (or `Utf8JsonWriter` directly to pipe) |
| Deserialize | `json.loads(...)` → `dict` | `JsonDocument.Parse(utf8Bytes)` → `JsonDocument` (disposable, scoped) |

**Optimization**: For IPC, write JSON directly to the `NamedPipeServerStream` using `Utf8JsonWriter` to avoid intermediate `byte[]` allocation.

#### Hot Path 3: Scheduler Loop (every 30 seconds)

| Step | Python | C# |
|------|--------|----|
| Get current time | `datetime.now()` → `datetime` object | `DateTime.UtcNow` (value type, no alloc) |
| String comparison | `current_time == morning_check` | `string.Equals(a, b)` (interned strings in config) |

**No significant allocations** — `DateTime` is a struct.

### 4.4 IPC Buffer Strategy

```csharp
// Using ArrayPool for IPC buffers (4 KiB as per original BUFFER_SIZE = 4096)
byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    int bytesRead = await pipe.ReadAsync(buffer, 0, 4096, cancellationToken);
    // Process buffer.AsSpan(0, bytesRead) — no additional alloc
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

### 4.5 Pinned Packages Cache Strategy

```csharp
// Reuse HashSet across checks
private readonly HashSet<string> _pinnedPackages = new();

private void RefreshPinnedPackages()
{
    _pinnedPackages.Clear(); // Reuse, don't reallocate
    // ... populate from winget pin list output
}
```

---

## 5. Error and Logging Strategy

### 5.1 Logging

**Framework**: `Microsoft.Extensions.Logging` with `ILogger<T>`.

**Zero-Alloc Logging**: Use `LoggerMessage` source generators for hot paths:

```csharp
// In UpdateChecker.cs — zero-alloc logging definitions
internal static partial class LogMessages
{
    [LoggerMessage(LogLevel.Information, "Update check completed. Found {UpdateCount} updates.")]
    public static partial void UpdateCheckCompleted(ILogger logger, int updateCount);

    [LoggerMessage(LogLevel.Error, "Winget update command failed with return code {ReturnCode}")]
    public static partial void WingetCommandFailed(ILogger logger, int returnCode);

    [LoggerMessage(LogLevel.Debug, "Parsing winget output line: {Line}")]
    public static partial void ParsingLine(ILogger logger, string line);
}
```

**Usage**:
```csharp
// Instead of: _logger.LogInformation($"Found {updateCount} updates");
LogMessages.UpdateCheckCompleted(_logger, updateCount); // No string allocation
```

### 5.2 Error Handling

| Pattern | Python | C# |
|---------|--------|----|
| Return error codes | `return 0` (no updates) | `return 0` (int) or `Result<int>` pattern |
| Exception handling | `try/except Exception as e` | `try/catch (Exception ex)` |
| IPC error response | `IPCMessage("error", {"message": str(e)})` | Return `IpcMessage` with error data |
| Winget not installed | Log error, return 0 | Same — catch `FileNotFoundException` from Process.Start |

**Decision**: Use exceptions for exceptional cases (winget not found, IPC broken), and return types (`int`, `bool`) for expected outcomes. Do NOT use a `Result<T>` monad — this adds unnecessary complexity for this project.

### 5.3 Structured Logging Events

| Event | Level | Message |
|-------|-------|---------|
| Service start | Information | "Winget Updater Service starting" |
| Update check start | Debug | "Starting update check (includePinned: {IncludePinned}, includeUnknown: {IncludeUnknown})" |
| Update check complete | Information | "Update check completed. Found {UpdateCount} updates." |
| Winget not found | Error | "Winget CLI not found at expected path" |
| IPC client connected | Information | "Connected to IPC server" |
| Settings saved | Information | "Settings updated via IPC" |
| Scheduler tick | Debug | "Scheduler check at {CurrentTime}, autoCheck: {AutoCheck}" |

---

## 6. Testing Strategy

### 6.1 Test Framework

| Tool | Purpose |
|------|---------|
| **xUnit** | Test framework (preferred for .NET, good async support) |
| **FluentAssertions** | Expressive assertions (`result.Should().Be(5)`) |
| **Microsoft.Extensions.Logging.Testing** | Mock `ILogger<T>` for verification |
| **Moq** or **NSubstitute** | Mock interfaces (`IConfigManager`, `IUpdateChecker`) |

### 6.2 Test Projects

```
tests/WinGup.Tests/
├── WinGup.Tests.csproj
├── UpdateCheckerTests.cs       # Tests for winget output parsing
├── ConfigManagerTests.cs       # Tests for INI read/write
├── IpcTests.cs                 # Tests for IPC message serialization
├── ParserTests.cs              # Tests for JSON and text parsers
├── ServiceTests.cs             # Tests for service lifecycle
└── TestHelpers/
    ├── MockUpdateChecker.cs    # Test double for IUpdateChecker
    └── SampleWingetOutput.cs   # Sample winget output strings
```

### 6.3 Behavioral Parity Tests

These tests verify that the C# port behaves identically to the Python original:

```csharp
public class UpdateCheckerParityTests
{
    [Fact]
    public async Task CheckUpdates_WhenWingetReturnsJson_ReturnsCorrectCount()
    {
        // Arrange: mock winget JSON output
        const string jsonOutput = /* sample JSON from Python tests */;
        var checker = CreateCheckerWithMockProcess(jsonOutput);

        // Act
        int count = await checker.CheckUpdatesAsync();

        // Assert: same count as Python version returns
        count.Should().Be(3); // Match Python behavior
    }

    [Fact]
    public async Task ParseWingetTextOutput_HandlesTruncatedIds()
    {
        // Arrange: text output with ID ending in '.'
        const string textOutput = "App Name  App.Id.  1.0  2.0  winget";

        // Act
        var updates = await ParseTextOutput(textOutput);

        // Assert: ID should be expanded (matching Python behavior)
        updates.Should().Contain(u => u.Id == "App.Id.Actual");
    }

    [Fact]
    public void ConfigManager_LoadsDefaults_WhenFileMissing()
    {
        // Arrange: no settings.ini
        var config = new ConfigManager("nonexistent.ini");

        // Assert: defaults match Python configparser defaults
        config.MorningCheckTime.Should().Be("08:00");
        config.AfternoonCheckTime.Should().Be("16:00");
        config.AutoCheck.Should().BeTrue();
    }
}
```

### 6.4 Coverage Target

- **Minimum**: 80% line coverage
- **Goal**: 90%+ for `UpdateChecker`, `ConfigManager`, `IpcServer`, `IpcClient`
- **Exclude**: Program.cs (entry point), TrayApplication.cs (UI — harder to unit test)

### 6.5 BenchmarkDotNet for Hot Path

```csharp
[MemoryDiagnoser]
public class UpdateParsingBenchmarks
{
    private string _jsonOutput = /* sample winget JSON */;
    private string _textOutput = /* sample winget text */;

    [Benchmark]
    public int ParseJson_StringPath()
    {
        // Naive: JsonSerializer.Deserialize<string>(...) → allocate strings
        var doc = JsonDocument.Parse(_jsonOutput);
        return doc.RootElement.GetProperty("Data").GetArrayLength();
    }

    [Benchmark]
    public int ParseJson_SpanPath()
    {
        // Optimized: Utf8JsonReader over ReadOnlySpan<byte>
        var bytes = Encoding.UTF8.GetBytes(_jsonOutput);
        var reader = new Utf8JsonReader(bytes);
        // ... manual parsing with minimal alloc
        return 0; // actual count
    }

    [Benchmark]
    public void ParseText_StringSplit()
    {
        // Naive: string.Split() → string[] per line
        foreach (var line in _textOutput.Split('\n'))
        {
            var parts = line.Split(new[] { "  " }, StringSplitOptions.None);
        }
    }

    [Benchmark]
    public void ParseText_SpanSlicing()
    {
        // Optimized: ReadOnlySpan<char> slicing
        ReadOnlySpan<char> span = _textOutput.AsSpan();
        // ... manual span scanning
    }
}
```

**Expected Results**: Show that `Span`-based parsing uses ~50-80% less memory than string-based parsing.

---

## 7. NuGet Dependencies (Justification)

| Package | Version | Purpose | Justification |
|---------|---------|---------|---------------|
| `System.IO.Pipes` | Built-in | Named Pipe IPC | .NET runtime, no additional dep |
| `System.Text.Json` | Built-in (.NET 8) | JSON parsing | High-performance, low-alloc, built-in |
| `Microsoft.Extensions.Logging` | Built-in (.NET 8) | Logging | Standard .NET logging, built-in |
| `Microsoft.Extensions.Hosting.WindowsServices` | Built-in (.NET 8) | Windows Service | Modern way to build Windows Services in .NET |
| `System.Drawing.Common` | 8.0+ | Icon generation | For dynamic tray icon with version overlay (like Pillow) |
| `Windows.Forms` | Built-in (.NET 8) | System tray + UI | `NotifyIcon` for system tray, forms for settings window |
| **Optional** | | | |
| `FluentAssertions` | 6.x | Test assertions | More readable tests |
| `Moq` | 4.x | Mocking | For unit tests |
| `BenchmarkDotNet` | 0.13.x | Benchmarks | For hot path analysis |
| `System.CommandLine` | 2.x | CLI parsing | Richer than manual `args` parsing |

**Decision**: Minimize external deps. Use built-in .NET 8 packages where possible. Add `FluentAssertions`, `Moq`, `BenchmarkDotNet` only for test/benchmark projects.

---

## 8. Substitutions Where C# Solves Better

| Python Approach | C# Better Approach | Reason | Documented In |
|-----------------|-------------------|--------|---------------|
| `configparser` (INI) | Custom `IniFile` class | `Microsoft.Extensions.Configuration.Ini` doesn't preserve comments/format; custom is simpler | DESIGN.md §3.1 |
| `json.dumps/loads` | `System.Text.Json` `Utf8JsonReader` | Zero-alloc option, built-in | DESIGN.md §4.3 |
| `subprocess.run()` | `Process.Start()` with async reading | Native .NET, better async support | DESIGN.md §3.1 |
| `threading.Thread` | `Task.Run()` / `BackgroundService` | Better integration with .NET async ecosystem | DESIGN.md §3.1 |
| `pystray` + `tkinter` | `NotifyIcon` + WinForms | Native Windows support, no extra deps | ADR-0003 |
| `time.sleep()` | `await Task.Delay()` | Cancellable, works with CancellationToken | DESIGN.md §3.1 |
| `schedule` library | `System.Threading.Timer` | Built-in, no extra dependency | DESIGN.md §3.1, ANALYSIS.md Open Question 1 |
| `re.split()` regex | `ReadOnlySpan<char>` slicing | No regex overhead, no string alloc | DESIGN.md §4.3 |

---

## 9. Summary of Key Design Decisions

1. **.NET 8 LTS** — Long-term support, performance improvements, built-in `System.Text.Json`
2. **`BackgroundService` + `WindowsServiceLifetime`** — Modern Windows Service pattern
3. **`readonly record struct` for `UpdateInfo`** — Zero heap allocation per update in hot paths
4. **`Span<T>` parsing** — Minimal allocations when parsing winget output
5. **`ILogger<T>` + `LoggerMessage`** — Zero-alloc structured logging
6. **WinForms for UI** — Native Windows tray support via `NotifyIcon`, no external UI framework needed
7. **`NamedPipeServerStream`/`NamedPipeClientStream`** — Direct replacement for `win32pipe` APIs
8. **xUnit + FluentAssertions** — Expressive, async-friendly testing
9. **Minimal NuGet dependencies** — Prefer built-in .NET 8 packages
