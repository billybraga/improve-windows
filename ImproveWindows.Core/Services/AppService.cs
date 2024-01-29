namespace ImproveWindows.Core.Services;

public abstract class AppService
{
    private string? _status;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StartAsync(cancellationToken);
        }
        catch (Exception e)
        {
            SetStatus("Error", true);
            LogError(e);
        }
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
        OnStatusChange?.Invoke(this, new StatusChangeEventArgs { Status = status ?? "Ok", IsError = isError });
        
        if (status != null && status != _status)
        {
            LogInfo(status);
        }

        _status = status;
    }

    protected void SetStatusKey(string statusKey, string? statusMessage = null, bool isError = false)
    {
        OnStatusChange?.Invoke(this, new StatusChangeEventArgs { Status = statusMessage ?? "Ok", IsError = isError });
        
        if (statusMessage != null && statusKey != _status)
        {
            LogInfo(statusMessage);
        }

        _status = statusKey;
    }
}