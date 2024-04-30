using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace ImproveWindows.Ui;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public sealed partial class App : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().AddEventLog());
    private readonly ILogger _logger;

    public App()
    {
        _logger = _loggerFactory.CreateLogger<App>();
        
        _logger.LogInformation("Starting");

        var otherProcesses = GetOtherProcess();

        if (otherProcesses.Count == 0)
        {
            return;
        }

        var kill = Environment.GetCommandLineArgs().Any(x => x == "--kill");
        var overtake = Environment.GetCommandLineArgs().Any(x => x == "--overtake");
        var ifNotRunning = Environment.GetCommandLineArgs().Any(x => x == "--if-not-running");
        var ids = string.Join(", ", otherProcesses.Select(x => x.Id));

        foreach (var otherProcess in otherProcesses)
        {
            if (kill || overtake)
            {
                try
                {
                    otherProcess.Kill();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            otherProcess.Dispose();
        }

        if (ifNotRunning)
        {
            throw new InvalidOperationException($"Already running on PID {ids}");
        }

        if (kill)
        {
            throw new InvalidOperationException($"Killed PID {ids}");
        }

        if (overtake)
        {
            _logger.LogInformation("Killed PID {Ids}", ids);
            return;
        }

        throw new InvalidOperationException($"Process already running at PID {ids}");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.LogInformation("Exiting with {ApplicationExitCode}", e.ApplicationExitCode);
        base.OnExit(e);
    }

    private static IReadOnlyCollection<Process> GetOtherProcess()
    {
        var processId = Environment.ProcessId;
        var processes = Process.GetProcessesByName("ImproveWindows.Ui");
        return processes.Where(x => x.Id != processId).ToArray();
    }

    public void Dispose()
    {
        try
        {
            _logger.LogInformation("Disposing");
        }
        finally
        {
            _loggerFactory.Dispose();
        }
    }
}