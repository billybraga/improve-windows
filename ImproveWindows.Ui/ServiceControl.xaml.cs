using System.Windows.Media;

namespace ImproveWindows.Ui;

public partial class ServiceControl
{
    private const int MaxCharCount = 20 * 25;

    public ServiceControl()
    {
        InitializeComponent();
    }

    public void AddLog(string message)
    {
        var date = DateTime.Now;
        var completeMessage = $"[{date:HH:mm:ss}] {message}\n";
        Dispatcher.InvokeAsync(
            () => { Logs.Text = completeMessage + GetLogsSubstring(); }
        );
    }

    private string GetLogsSubstring()
    {
        return Logs.Text.Length > MaxCharCount
            ? Logs.Text.Substring(0, Logs.Text.IndexOf('\n', MaxCharCount))
            : Logs.Text;
    }

    public void SetStatus(string status, bool isError)
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                Status.Content = status;
                Status.Foreground = isError ? Brushes.Red : Brushes.Green;
            }
        );
    }
}