using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImproveWindows.Core.Services.CommandRunner;

namespace ImproveWindows.Ui;

internal partial class CommandRunnerControl
{
    private readonly Dictionary<Guid, CommandRow> _rows = new();
    private CommandRunnerService? _service;

    public event EventHandler<RoutedEventArgs>? OnRestartClick;
    public event EventHandler<RoutedEventArgs>? OnStopClick;

    public CommandRunnerControl()
    {
        InitializeComponent();
    }

    public void Initialize(CommandRunnerService service)
    {
        _service = service;
        service.CommandAdded += (_, process) => Dispatcher.Invoke(() => AddRow(process));
        service.CommandRemoved += (_, process) => Dispatcher.Invoke(() => RemoveRow(process));
    }

    public void SetStatus(string status, bool isError)
    {
        _ = Dispatcher.InvokeAsync(
            () =>
            {
                Status.Content = status;
                Status.Foreground = isError ? Brushes.Red : Brushes.Green;
            }
        );
    }

    private void AddRow(CommandProcess process)
    {
        var row = new CommandRow(process.Definition.CommandLine);

        process.OnOutput += async (_, args) => await row.AddLogAsync(args.Message);
        process.OnStatusChange += (_, args) => row.SetStatus(args.Status, args.IsError);

        row.OnRestartClick += (_, _) => _ = process.RestartAsync();
        row.OnStopClick += (_, _) => _ = process.StopAsync();
        row.OnDeleteClick += (_, _) => _ = _service?.RemoveCommandAsync(process.Definition.Id);

        _rows[process.Definition.Id] = row;
        _ = CommandsPanel.Children.Add(row);
    }

    private void RemoveRow(CommandProcess process)
    {
        if (_rows.Remove(process.Definition.Id, out var row))
        {
            CommandsPanel.Children.Remove(row);
        }
    }

    private void AddBtnClick(object sender, RoutedEventArgs e)
    {
        SubmitNewCommand();
    }

    private void RestartFailedBtnClick(object sender, RoutedEventArgs e)
    {
        _ = _service?.RestartFailedAsync();
    }

    private void RestartBtnClick(object sender, RoutedEventArgs e)
    {
        OnRestartClick?.Invoke(sender, e);
    }

    private void StopBtnClick(object sender, RoutedEventArgs e)
    {
        OnStopClick?.Invoke(sender, e);
    }

    private void NewCommandInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitNewCommand();
        }
    }

    private void SubmitNewCommand()
    {
        var commandLine = NewCommandInput.Text.Trim();
        if (string.IsNullOrEmpty(commandLine) || _service == null)
        {
            return;
        }

        _service.AddCommand(commandLine);
        NewCommandInput.Text = string.Empty;
    }
}
