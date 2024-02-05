using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;

namespace ImproveWindows.Ui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public sealed partial class MainWindow : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<ServiceInfos> _taskInfos = new();
    private readonly WindowInteropHelper _windowInteropHelper;

    private record ServiceInfos
    {
        public string Name { get; }
        public Task Task { get; private set; }
        public AppService Service { get; }
        public ServiceControl ServiceControl { get; }
        
        public ServiceInfos(string name, Task task, AppService service, ServiceControl serviceControl)
        {
            Name = name;
            Task = task;
            Service = service;
            ServiceControl = serviceControl;
        }

        public void Restart()
        {
            Task = Service.RestartAsync();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        
        _windowInteropHelper = new WindowInteropHelper(this);
        
#pragma warning disable CA2000
        var audioLevels = new AudioLevelsService();
        StartService("MicMute", new MicMute(audioLevels));
        StartService("AudioLevels", audioLevels);
        StartService("Network", new NetworkService());
        StartService("Memory", new MemoryService());
        StartService("Window", new WindowService());
#pragma warning restore CA2000

        void StartService(string name, AppService service)
        {
            var serviceControl = new ServiceControl
            {
                ServiceName =
                {
                    Content = name,
                },
            };

            service.OnLog += (_, args) => serviceControl.AddLog(args.Message);
            service.OnStatusChange += (_, args) =>
            {
                serviceControl.SetStatus(args.Status, args.IsError);
                if (args is { IsError: true, WasAlreadyError: false })
                {
                    PInvoke.FlashWindow(new HWND(_windowInteropHelper.Handle), true);
                }
            };

            var serviceInfos = new ServiceInfos(name, service.RunAsync(_cancellationTokenSource.Token), service, serviceControl);
            _taskInfos.Add(serviceInfos);

            serviceControl.OnRestartClick += (_, _) =>
            {
                serviceInfos.Restart();
            };
            
            MainGrid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
            
            MainGrid.Children.Add(serviceControl);
            
            serviceControl.SetValue(Grid.ColumnProperty, MainGrid.Children.Count - 1);
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

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}