using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ImproveWindows.Core;
using ImproveWindows.Core.Services;
using ImproveWindows.Core.Services.CommandRunner;
using ImproveWindows.Ui.Webcam;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ImproveWindows.Ui;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
internal sealed partial class MainWindow : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<ServiceInfos> _taskInfos = [];
    private readonly WindowInteropHelper _windowInteropHelper;
    private WebcamSettingsWindow? _webcamSettingsWindow;

    private sealed record ServiceInfos
    {
        public Task Task { get; private set; }
        public AppService Service { get; }

        public ServiceInfos(Task task, AppService service)
        {
            Task = task;
            Service = service;
        }

        public void Restart()
        {
            Task = Service.RestartAsync();
        }

        public void Stop()
        {
            Task = Service.StopAsync();
        }
    }

    public MainWindow()
    {
        InitializeComponent();

        _windowInteropHelper = new WindowInteropHelper(this);

#pragma warning disable CA2000
        // StartService("Vpn", new VpnService());
        RegisterService("AudioLevels", new AudioLevelsService());
        RegisterService("Window", new WindowService(), true);
        RegisterService("Network", new NetworkService());
        var webcam = new WebcamService();
        RegisterService("Webcam", webcam)
            .AddAction("⚙", "Camera settings", () => OpenWebcamSettings(webcam));
        // RegisterService("Memory", new MemoryService());
        // RegisterService("HdmiAudio", new HdmiAudioService());
        RegisterCommandRunnerService();
#pragma warning restore CA2000

        void RegisterCommandRunnerService()
        {
            var service = new CommandRunnerService(new CommandStore());
            var control = new CommandRunnerControl();
            control.Initialize(service);

            service.OnStatusChange += (_, args) =>
            {
                control.SetStatus(args.Status, args.IsError);
                if (args is { IsError: true, WasAlreadyError: false })
                {
                    PInvoke.FlashWindow(new HWND(_windowInteropHelper.Handle), true);
                }
            };

            var serviceInfos = new ServiceInfos(service.RunAsync(_cancellationTokenSource.Token), service);
            _taskInfos.Add(serviceInfos);

            control.OnRestartClick += (_, _) =>
            {
                serviceInfos.Restart();
            };

            control.OnStopClick += (_, _) =>
            {
                serviceInfos.Stop();
            };

            MainGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                }
            );

            _ = MainGrid.Children.Add(control);

            control.SetValue(Grid.ColumnProperty, MainGrid.Children.Count - 1);
        }

        ServiceControl RegisterService(string name, AppService service, bool start = true)
        {
            var serviceControl = new ServiceControl
            {
                ServiceName =
                {
                    Content = name,
                },
            };

            service.OnLog += async (_, args) => await serviceControl.AddLogAsync(args.Message);
            service.OnStatusChange += (_, args) =>
            {
                serviceControl.SetStatus(args.Status, args.IsError);
                if (args is { IsError: true, WasAlreadyError: false })
                {
                    PInvoke.FlashWindow(new HWND(_windowInteropHelper.Handle), true);
                }
            };

            var serviceInfos = new ServiceInfos(
                start ? service.RunAsync(_cancellationTokenSource.Token) : Task.CompletedTask,
                service
            );
            _taskInfos.Add(serviceInfos);

            serviceControl.OnRestartClick += (_, _) =>
            {
                serviceInfos.Restart();
            };

            serviceControl.OnStopClick += (_, _) =>
            {
                serviceInfos.Stop();
            };

            MainGrid.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                }
            );

            _ = MainGrid.Children.Add(serviceControl);

            serviceControl.SetValue(Grid.ColumnProperty, MainGrid.Children.Count - 1);

            return serviceControl;
        }
    }

    private void OpenWebcamSettings(WebcamService service)
    {
        if (_webcamSettingsWindow is not null)
        {
            _ = _webcamSettingsWindow.Activate();
            return;
        }

        // The settings window owns and disposes the camera
#pragma warning disable CA2000
        var camera = UvcCamera.Open(WebcamService.CameraNameMatch);
#pragma warning restore CA2000
        if (camera is null)
        {
            _ = MessageBox.Show(this, $"No camera matching {WebcamService.CameraNameMatch}", "Webcam settings");
            return;
        }

        _webcamSettingsWindow = new WebcamSettingsWindow(service, camera) { Owner = this };
        _webcamSettingsWindow.Closed += (_, _) => _webcamSettingsWindow = null;
        _webcamSettingsWindow.Show();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _cancellationTokenSource.Cancel();
        _ = Dispatcher.Invoke(
            DispatcherPriority.Background,
            async () =>
            {
                try
                {
                    await Task.WhenAll(_taskInfos.Select(x => x.Task).ToArray());
                }
                catch (OperationCanceledException) { }
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