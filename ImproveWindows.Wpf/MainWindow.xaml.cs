using System.ComponentModel;
using System.Windows.Threading;
using ImproveWindows.Core;
using ImproveWindows.Core.Logging;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<(string Name, Task Task, IAppService Service)> _taskInfos = new();

    public MainWindow()
    {
        InitializeComponent();

        var audioLevels = new AudioLevels(new Logger("AudioLevels", Write));
        StartService("MicMute", new MicMute(audioLevels, new Logger("MicMute", Write)));
        StartService("AudioLevels", audioLevels);
        StartService("Network", new Network(new Logger("Network", Write)));
        StartService("Memory", new Memory(new Logger("Memory", Write)));

        void StartService(string name, IAppService service)
        {
            _taskInfos.Add((name, service.RunAsync(_cancellationTokenSource.Token), service));
        }
    }

    private void Write(string line)
    {
        Dispatcher.Invoke(
            () =>
            {
                TextBlock.Text += line;
                ScrollViewer.ScrollToBottom();
            }
        );
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cancellationTokenSource.Cancel();
        Dispatcher.Invoke(
            DispatcherPriority.Background,
            async () =>
            {
                try
                {
                    await Task.WhenAll(_taskInfos.Select(x => x.Task).ToArray());
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }
            }
        );
        _cancellationTokenSource.Dispose();
        base.OnClosing(e);
    }
}