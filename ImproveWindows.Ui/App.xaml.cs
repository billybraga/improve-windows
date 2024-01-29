using System.Diagnostics;

namespace ImproveWindows.Ui;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App
{
    public App()
    {
        var otherProcesses = GetOtherProcess();

        if (otherProcesses.Count == 0)
        {
            return;
        }

        var kill = Environment.GetCommandLineArgs().Any(x => x == "--kill");
        var overtake = Environment.GetCommandLineArgs().Any(x => x == "--overtake");
        var ids = string.Join(", ", otherProcesses.Select(x => x.Id));

        foreach (var otherProcess in otherProcesses)
        {
            if (kill || overtake)
            {
                otherProcess.Kill();
            }

            otherProcess.Dispose();
        }

        if (kill)
        {
            throw new InvalidOperationException($"Killed PID {ids}");
        }

        if (overtake)
        {
            Console.WriteLine($"Killed PID {ids}");
            return;
        }

        throw new InvalidOperationException($"Process already running at PID {ids}");
    }

    private static IReadOnlyCollection<Process> GetOtherProcess()
    {
        var processId = Environment.ProcessId;
        var processes = Process.GetProcessesByName("ImproveWindows.Ui");
        return processes.Where(x => x.Id != processId).ToArray();
    }
}