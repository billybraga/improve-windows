namespace ImproveWindows.Core.Logging;

public class Logger
{
    private readonly Action<string> _write;
    private const int MaxKeyLength = 12;
    private readonly string _key;

    public Logger(string key, Action<string> write)
    {
        _write = write;
        _key = '[' + key
            .Substring(0, Math.Min(MaxKeyLength, key.Length))
            .PadLeft(MaxKeyLength + 2)
            + "] ";
    }

    private void LogPrefix()
    {
        var date = DateTime.Now;
        _write($"[{date:yyyy-MM-dd HH:mm:ss}] ");
        _write(_key);
    }

    public void Log(string message, params object[] args)
    {
        LogPrefix();
        _write(string.Format(message, args));
        _write(Environment.NewLine);
    }

    public void Log(Exception exception)
    {
        LogPrefix();
        _write(exception.ToString());
        _write(Environment.NewLine);
    }
}