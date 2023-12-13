namespace ImproveWindows.Core.Services;

public abstract class AppService
{
    private string? _status;
    
    public abstract Task RunAsync(CancellationToken cancellationToken);

    public event EventHandler<TextMessageEventArgs>? OnLog;
    public event EventHandler<StatusEventArgs>? OnStatusChange;

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
        OnStatusChange?.Invoke(this, new StatusEventArgs { Status = status ?? "Ok", IsError = isError });
        
        if (isError && status != null && status != _status)
        {
            LogInfo(status);
        }

        _status = status;
    }
}