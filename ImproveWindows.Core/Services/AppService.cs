namespace ImproveWindows.Core.Services;

public abstract class AppService : IDisposable
{
    private string? _status;
    private CancellationToken _cancellationToken;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _task;

    public bool IsError { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        SetStatus("Starting");
        try
        {
            _cancellationToken = cancellationToken;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _task = StartAsync(_cancellationTokenSource.Token);
            await _task;
            SetStatus("Stopped");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            SetStatus("Error", true);
            LogError(e);
        }
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await RunAsync(_cancellationToken);
    }

    public async Task StopAsync()
    {
        LogInfo("Stopping");
        
        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
        }
        
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
        
        LogInfo("Stopped");
    }

    protected abstract Task StartAsync(CancellationToken cancellationToken);

    public event EventHandler<TextMessageEventArgs>? OnLog;
    public event EventHandler<StatusChangeEventArgs>? OnStatusChange;

    protected void LogInfo(string message)
    {
        OnLog?.Invoke(this, new TextMessageEventArgs { Message = message });
    }

    protected void LogError(Exception exception)
    {
        OnLog?.Invoke(this, new TextMessageEventArgs { Message = exception.ToString() });
    }

    protected void SetStatus(string? status = null, bool isError = false)
    {
        TriggerOnStatusChange(status, isError);

        if (status != null && status != _status)
        {
            LogInfo(status);
        }

        _status = status;
    }

    protected void SetStatusKey(string statusKey, string? statusMessage = null, bool isError = false)
    {
        TriggerOnStatusChange(statusMessage ?? "Ok", isError);

        if (statusMessage != null && statusKey != _status)
        {
            LogInfo(statusMessage);
        }

        _status = statusKey;
    }

    private void TriggerOnStatusChange(string? status, bool isError)
    {
        var wasAlreadyError = IsError;
        IsError = isError;
        OnStatusChange?.Invoke(this, new StatusChangeEventArgs { Status = status ?? "Ok", IsError = isError, WasAlreadyError = wasAlreadyError });
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}