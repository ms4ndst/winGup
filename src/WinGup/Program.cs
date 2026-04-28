using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WinGup;

/// <summary>
/// Entry point for the Winget Updater application.
/// </summary>
public class Program
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code (0 for success, 1 for failure)</returns>
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "standalone")
            {
                return RunStandaloneMode();
            }

            var command = args[0].ToLowerInvariant();
            return command switch
            {
                "install" => InstallService(),
                "uninstall" => UninstallService(),
                "start" => StartService(),
                "stop" => StopService(),
                "restart" => RestartService(),
                "service" => RunServiceOnly(),
                "ui" => RunUiOnly(),
                "debug" => RunDebugMode(),
                "add-autostart" => AutostartSetup(install: true),
                "remove-autostart" => AutostartSetup(install: false),
                _ => RunStandaloneMode()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unhandled exception: {ex.Message}");
            return 1;
        }
    }

    private static bool IsAdmin()
    {
        try
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void RunAsAdmin(string[] args)
    {
        if (!IsAdmin())
        {
            var exePath = Environment.ProcessPath ?? "winGup.exe";
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(startInfo);
        }
    }

    private static int InstallService()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("Error: Administrator privileges are required to install the service.");
            return 1;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"create WingetUpdaterService binPath= \"{Environment.ProcessPath}\" DisplayName= \"Winget Updater Service\" start= auto",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            Console.WriteLine("Service installed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error installing service: {ex.Message}");
            return 1;
        }
    }

    private static int UninstallService()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("Error: Administrator privileges are required to uninstall the service.");
            return 1;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "delete WingetUpdaterService",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            Console.WriteLine("Service uninstalled successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error uninstalling service: {ex.Message}");
            return 1;
        }
    }

    private static int StartService()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("Error: Administrator privileges are required to start the service.");
            return 1;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "start WingetUpdaterService",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            Console.WriteLine("Service started successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting service: {ex.Message}");
            return 1;
        }
    }

    private static int StopService()
    {
        if (!IsAdmin())
        {
            Console.Error.WriteLine("Error: Administrator privileges are required to stop the service.");
            return 1;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "stop WingetUpdaterService",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit();

            Console.WriteLine("Service stopped successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error stopping service: {ex.Message}");
            return 1;
        }
    }

    private static int RestartService()
    {
        StopService();
        Thread.Sleep(1000);
        return StartService();
    }

    private static int RunServiceOnly()
    {
        try
        {
            var host = CreateHostBuilder(Array.Empty<string>()).Build();
            host.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running service: {ex.Message}");
            return 1;
        }
    }

    private static int RunUiOnly()
    {
        Console.WriteLine("UI mode not yet implemented in this port.");
        return 0;
    }

    private static int RunStandaloneMode()
    {
        try
        {
            // Initialize WinForms first
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var host = CreateHostBuilder(Array.Empty<string>()).Build();
            host.Start();

            Console.WriteLine("Service running, press Ctrl+C to stop");

            // Create and run tray application on the main thread
            var ipcClient = host.Services.GetRequiredService<IIpcClient>();
            var logger = host.Services.GetRequiredService<ILogger<TrayApplication>>();
            var updateChecker = host.Services.GetRequiredService<IUpdateChecker>();
            var trayApp = new TrayApplication(ipcClient, logger, updateChecker);
            trayApp.Run();  // This starts IPC listener and WinForms message loop

            host.StopAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in standalone mode: {ex.Message}");
            return 1;
        }
    }

    private static int RunDebugMode()
    {
        Console.WriteLine("Starting Winget Updater in debug mode...");
        try
        {
            // Initialize WinForms
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var host = CreateHostBuilder(Array.Empty<string>()).Build();
            host.Start();

            Console.WriteLine("Service running, press Ctrl+C to stop");

            // Start tray application on a separate thread
            var trayThread = new Thread(() =>
            {
                var ipcClient = host.Services.GetRequiredService<IIpcClient>();
                var logger = host.Services.GetRequiredService<ILogger<TrayApplication>>();
                var trayApp = new TrayApplication(ipcClient, logger);
                trayApp.Run();
            });
            trayThread.SetApartmentState(ApartmentState.STA);
            trayThread.Start();

            Console.ReadLine();

            host.StopAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error running in debug mode: {ex.Message}");
            return 1;
        }
    }

    private static int AutostartSetup(bool install)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)!;

            var appName = "WingetUpdater";

            if (install)
            {
                var exePath = Environment.ProcessPath ?? "winGup.exe";
                key.SetValue(appName, $"\"{exePath}\" --ui");
                Console.WriteLine($"Added {appName} to autostart");
            }
            else
            {
                try
                {
                    key.DeleteValue(appName);
                    Console.WriteLine($"Removed {appName} from autostart");
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"{appName} was not in autostart");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error managing autostart: {ex.Message}");
            return 1;
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseWindowsService()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                });

                services.AddSingleton<IConfigManager, ConfigManager>();
                services.AddSingleton<IUpdateChecker, UpdateChecker>();
                services.AddSingleton<IpcServer>();
                services.AddSingleton<IIpcClient, IpcClient>();
                services.AddHostedService<WingetUpdaterService>();
            });
    }
}
