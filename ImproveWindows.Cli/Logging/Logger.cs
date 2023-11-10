namespace ImproveWindows.Cli.Logging;

public class Logger
{
    private const int MaxKeyLength = 12;
    private readonly string _key;

    public Logger(string key)
    {
        _key = '[' + key
            .Substring(0, Math.Min(MaxKeyLength, key.Length))
            .PadLeft(MaxKeyLength + 2)
            + "] ";
    }

    private void LogPrefix()
    {
        var date = DateTime.Now;
        Console.Write($"[{date:yyyy-MM-dd HH:mm:ss}] ");
        Console.Write(_key);
    }

    public void Log(string message, params object[] args)
    {
        LogPrefix();
        Console.WriteLine(message, args);
    }

    public void Log(Exception exception)
    {
        LogPrefix();
        Console.WriteLine(exception);
    }
}