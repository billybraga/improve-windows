using System.Windows;
using System.Windows.Media;

namespace ImproveWindows.Ui;

public partial class ServiceControl
{
#if DEBUG
    private const int LineCount = 300;
#else
    private const int LineCount = 30;
#endif
    private const int MaxCharCount = LineCount * 40;

    public event EventHandler<RoutedEventArgs>? OnRestartClick;

    public ServiceControl()
    {
        InitializeComponent();
    }

    public async Task AddLogAsync(string message)
    {
        var date = DateTime.Now;
        var completeMessage = $"[{date:HH:mm:ss.fff}] {message}\n";
        await Dispatcher.InvokeAsync(
            () => { Logs.Text = completeMessage + GetLogsSubstring(); }
        );
    }

    private string GetLogsSubstring()
    {
        if (Logs.Text.Length > MaxCharCount)
        {
            var indexOfNewLine = Logs.Text.IndexOf('\n', MaxCharCount);
            if (indexOfNewLine > 0)
            {
                return Logs.Text.Substring(0, indexOfNewLine);
            }
        }

        return Logs.Text;
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

    private void RestartBtnClick(object sender, RoutedEventArgs e)
    {
        OnRestartClick?.Invoke(sender, e);
    }
}