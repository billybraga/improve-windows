using System.Windows.Controls;
using System.Windows.Media;

namespace ImproveWindows.Wpf;

public partial class ServiceControl : UserControl
{
    public ServiceControl()
    {
        InitializeComponent();
    }

    public void AddLog(string message)
    {
        var date = DateTime.Now;
        var completeMessage = $"[{date:HH:mm:ss}] {message}\n";
        Dispatcher.InvokeAsync(
            () =>
            {
                Logs.Text += completeMessage;
                ScrollViewer.ScrollToBottom();
            }
        );
    }

    public void SetStatus(string status, bool isError)
    {
        Dispatcher.InvokeAsync(
            () =>
            {
                Status.Content = status;
                Status.Foreground = isError ? Brushes.Red : Brushes.Black;
            }
        );
    }
}