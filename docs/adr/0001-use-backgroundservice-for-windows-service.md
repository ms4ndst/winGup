# ADR-0001: Use BackgroundService for Windows Service

## Status
Accepted

## Context
The Python Winget_Updater uses pywin32's `win32serviceutil.ServiceFramework` to implement a Windows Service. We need to port this to C# while maintaining behavioral parity.

Options considered:
1. **TopShelf** - Popular .NET service framework, but adds dependency
2. **Windows Service Project type** - Old .NET Framework approach, not recommended for .NET 8
3. **BackgroundService + UseWindowsService()** - Modern .NET 8 approach using Microsoft.Extensions.Hosting

## Decision
We will use `BackgroundService` from Microsoft.Extensions.Hosting with the `UseWindowsService()` extension method.

## Consequences

### Positive
- Native .NET 8 support without external dependencies
- Clean integration with dependency injection
- Easy to test (can run as console app or service)
- Automatic handling of service lifecycle (OnStart, OnStop, OnPause)
- Same codebase works as service or standalone app

### Negative
- Different from Python's pywin32 approach (but behavior is equivalent)
- Requires understanding of .NET Generic Host model

### Neutral
- Service installation still requires `sc create` or similar (not handled by BackgroundService itself)

## Implementation Notes
- `WingetUpdaterService.cs` inherits from `BackgroundService`
- `ExecuteAsync()` contains the main loop with `_cancellationTokenSource.Token.WaitHandle.WaitOne()`
- `Program.cs` calls `UseWindowsService()` when building the host
- Service can be run as:
  - Windows Service: `sc start WinGup`
  - Console app: `WinGup.exe --standalone`

## References
- [.NET 8 Windows Service with BackgroundService](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- Python original: `service_component.py` using `win32serviceutil.ServiceFramework`
