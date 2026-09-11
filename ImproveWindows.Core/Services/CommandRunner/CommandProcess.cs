using CliWrap;

namespace ImproveWindows.Core.Services.CommandRunner;

/// <summary>
/// Runs a single user-defined shell command line, tracking whether it is running or has exited.
/// </summary>
public sealed class CommandProcess : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _task;

    public ManagedCommand Definition { get; }
    public bool IsRunning { get; private set; }

    public event EventHandler<TextMessageEventArgs>? OnOutput;
    public event EventHandler<StatusChangeEventArgs>? OnStatusChange;

    public CommandProcess(ManagedCommand definition)
    {
        Definition = definition;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        RaiseStatus("Running");

        _cancellationTokenSource = new CancellationTokenSource();
        _task = RunAsync(_cancellationTokenSource.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await Cli.Wrap("cmd.exe")
                .WithArguments(["/d", "/c", Definition.CommandLine])
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(RaiseOutput))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(RaiseOutput))
                .ExecuteAsync(cancellationToken);

            IsRunning = false;
            RaiseStatus($"Exited ({result.ExitCode})", result.ExitCode != 0);
        }
        catch (OperationCanceledException)
        {
            IsRunning = false;
            RaiseStatus("Stopped");
        }
        catch (Exception e)
        {
            IsRunning = false;
            RaiseOutput(e.ToString());
            RaiseStatus("Error", true);
        }
    }

    public async Task StopAsync()
    {
        if (_cancellationTokenSource == null)
        {
            return;
        }

        await _cancellationTokenSource.CancelAsync();

        if (_task != null)
        {
            try
            {
                await _task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        Start();
    }

    private void RaiseOutput(string line)
    {
        OnOutput?.Invoke(this, new TextMessageEventArgs { Message = line });
    }

    private void RaiseStatus(string status, bool isError = false)
    {
        OnStatusChange?.Invoke(this, new StatusChangeEventArgs { Status = status, IsError = isError, WasAlreadyError = false });
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Dispose();
    }
}
