namespace ImproveWindows.Core.Services.CommandRunner;

/// <summary>
/// Hosts an arbitrary set of user-defined shell commands, persisting them to disk so they
/// are restarted the next time the app runs.
/// </summary>
public sealed class CommandRunnerService : AppService
{
    private readonly CommandStore _store;
    private readonly List<CommandProcess> _commands = [];
    private readonly HashSet<Guid> _erroredCommands = [];

    public event EventHandler<CommandProcess>? CommandAdded;
    public event EventHandler<CommandProcess>? CommandRemoved;

    public CommandRunnerService(CommandStore store)
    {
        _store = store;
    }

    protected override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var definition in _store.Load())
        {
            AddCommandProcess(definition);
        }

        SetStatus("Running");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        finally
        {
            await StopAllAsync();
        }
    }

    public CommandProcess AddCommand(string commandLine)
    {
        var definition = new ManagedCommand { Id = Guid.NewGuid(), CommandLine = commandLine };
        var process = AddCommandProcess(definition);
        Persist();
        return process;
    }

    public async Task RemoveCommandAsync(Guid id)
    {
        CommandProcess? process;
        lock (_commands)
        {
            process = _commands.Find(c => c.Definition.Id == id);
            if (process != null)
            {
                _commands.Remove(process);
            }
        }

        if (process == null)
        {
            return;
        }

        process.OnStatusChange -= OnCommandStatusChange;
        await process.StopAsync();
        CommandRemoved?.Invoke(this, process);
        process.Dispose();
        Persist();

        lock (_commands)
        {
            _erroredCommands.Remove(id);
        }

        UpdateAggregateStatus();
    }

    private CommandProcess AddCommandProcess(ManagedCommand definition)
    {
        var process = new CommandProcess(definition);
        process.OnStatusChange += OnCommandStatusChange;
        lock (_commands)
        {
            _commands.Add(process);
        }

        CommandAdded?.Invoke(this, process);
        process.Start();
        return process;
    }

    private void OnCommandStatusChange(object? sender, StatusChangeEventArgs args)
    {
        if (sender is not CommandProcess process)
        {
            return;
        }

        lock (_commands)
        {
            if (args.IsError)
            {
                _erroredCommands.Add(process.Definition.Id);
            }
            else
            {
                _erroredCommands.Remove(process.Definition.Id);
            }
        }

        UpdateAggregateStatus();
    }

    private void UpdateAggregateStatus()
    {
        int erroredCount;
        lock (_commands)
        {
            erroredCount = _erroredCommands.Count;
        }

        SetStatus(erroredCount > 0 ? $"{erroredCount} command(s) failed" : "Running", erroredCount > 0);
    }

    private void Persist()
    {
        List<ManagedCommand> definitions;
        lock (_commands)
        {
            definitions = _commands.ConvertAll(c => c.Definition);
        }

        _store.Save(definitions);
    }

    private async Task StopAllAsync()
    {
        List<CommandProcess> commands;
        lock (_commands)
        {
            commands = [.._commands];
        }

        await Task.WhenAll(commands.Select(c => c.StopAsync()));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_commands)
            {
                foreach (var command in _commands)
                {
                    command.Dispose();
                }
            }
        }

        base.Dispose(disposing);
    }
}
