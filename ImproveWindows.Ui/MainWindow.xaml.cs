using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<ServiceInfos> _taskInfos = new();

    private record ServiceInfos(string Name, Task Task, AppService Service, ServiceControl ServiceControl);
    
    public MainWindow()
    {
        InitializeComponent();
        
        var audioLevels = new AudioLevelsService();
        StartService("MicMute", new MicMute(audioLevels));
        StartService("AudioLevels", audioLevels);
        StartService("Network", new NetworkService());
        StartService("Memory", new MemoryService());
        StartService("Window", new WindowService());

        void StartService(string name, AppService service)
        {
            var serviceControl = new ServiceControl
            {
                Name =
                {
                    Content = name,
                },
            };
            
            service.OnLog += (_, args) => serviceControl.AddLog(args.Message);
            service.OnStatusChange += (_, args) => serviceControl.SetStatus(args.Status, args.IsError);
            
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            
            MainGrid.Children.Add(serviceControl);
            
            serviceControl.SetValue(Grid.ColumnProperty, MainGrid.Children.Count - 1);
            
            _taskInfos.Add(new ServiceInfos(name, service.RunAsync(_cancellationTokenSource.Token), service, serviceControl));
        }
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