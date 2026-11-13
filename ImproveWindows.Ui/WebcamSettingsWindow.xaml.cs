using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ImproveWindows.Ui.Webcam;

namespace ImproveWindows.Ui;

/// <summary>
/// Sliders writing straight to the camera, so the change is visible live in any app using it.
/// The webcam service stops enforcing the saved settings while this is open.
/// </summary>
internal sealed partial class WebcamSettingsWindow
{
    private readonly WebcamService _service;
    private readonly UvcCamera _camera;
    private readonly List<Row> _rows = [];
    private readonly DispatcherTimer _refreshTimer;
    private bool _isRefreshing;
    private WebcamPreview? _preview;
    private bool _isClosed;

    private sealed record Row(WebcamControl Control, Slider Slider, Button Minus, Button Plus, Label Value, CheckBox? Auto)
    {
        public bool IsEnabled
        {
            get => Slider.IsEnabled;
            set => Slider.IsEnabled = Minus.IsEnabled = Plus.IsEnabled = value;
        }

        public WebcamSetting Setting => Control.Setting with
        {
            Value = (int) Math.Round(Slider.Value),
            Auto = Auto?.IsChecked == true,
        };
    }

    public WebcamSettingsWindow(WebcamService service, UvcCamera camera)
    {
        InitializeComponent();

        _service = service;
        _camera = camera;
        CameraName.Content = camera.Name;

        foreach (var control in camera.GetControls())
        {
            AddRow(control);
        }

        UpdateEnabledStates();

        // Shows the values the camera picks itself for auto settings
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => RefreshCameraDrivenValues(), Dispatcher);
        _refreshTimer.Start();

        _service.BeginEditing();

        Loaded += async (_, _) => await StartPreviewAsync();
    }

    private async Task StartPreviewAsync()
    {
        try
        {
            var preview = await WebcamPreview.StartAsync(
                WebcamService.CameraNameMatch,
                Dispatcher,
                bitmap =>
                {
                    Preview.Source = bitmap;
                    PreviewStatus.Visibility = Visibility.Collapsed;
                }
            );

            if (_isClosed)
            {
                if (preview is not null)
                {
                    await preview.DisposeAsync();
                }

                return;
            }

            _preview = preview;
            if (preview is null)
            {
                PreviewStatus.Text = "Camera not found for preview";
            }
        }
        catch (Exception e)
        {
            PreviewStatus.Text = $"No preview, the camera is probably used by another app\n{e.Message}";
        }
    }

    private void AddRow(WebcamControl control)
    {
        var rowIndex = ControlsGrid.RowDefinitions.Count;
        ControlsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new Label { Content = control.Setting.Property, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = control.Min,
            Maximum = control.Max,
            Value = control.Setting.Value,
            SmallChange = control.Step,
            LargeChange = Math.Max(control.Step, (control.Max - control.Min) / 10),
            TickFrequency = control.Step,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var minus = new Button { Content = "−", Width = 24, Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        var plus = new Button { Content = "+", Width = 24, Margin = new Thickness(4, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
        var value = new Label { Content = control.Setting.Value, VerticalAlignment = VerticalAlignment.Center };
        var auto = control.AutoSupported
            ? new CheckBox { Content = "Auto", IsChecked = control.Setting.Auto, VerticalAlignment = VerticalAlignment.Center }
            : null;

        var row = new Row(control, slider, minus, plus, value, auto);
        _rows.Add(row);

        // The slider clamps to its range and the ValueChanged handler applies it
        minus.Click += (_, _) => slider.Value -= control.Step;
        plus.Click += (_, _) => slider.Value += control.Step;

        slider.ValueChanged += (_, _) =>
        {
            value.Content = (int) Math.Round(slider.Value);
            if (!_isRefreshing)
            {
                Apply(row);
            }
        };

        if (auto is not null)
        {
            auto.Click += (_, _) =>
            {
                Apply(row);
                UpdateEnabledStates();
            };
        }

        UIElement?[] cells = [name, minus, slider, plus, value, auto];
        for (var column = 0; column < cells.Length; column++)
        {
            if (cells[column] is not { } cell)
            {
                continue;
            }

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, column);
            _ = ControlsGrid.Children.Add(cell);
        }
    }

    private void Apply(Row row)
    {
        var result = _camera.Set(row.Setting);
        row.Slider.ToolTip = result == 0 ? null : $"Error 0x{result:X8}";
    }

    private void UpdateEnabledStates()
    {
        var settings = _rows.Select(x => x.Setting).ToArray();
        foreach (var row in _rows)
        {
            var setting = row.Setting;
            row.IsEnabled = !setting.Auto && !setting.IsDrivenByCamera(settings);
        }
    }

    private void RefreshCameraDrivenValues()
    {
        var current = _camera.GetControls().ToDictionary(x => x.Setting.Property);

        _isRefreshing = true;
        try
        {
            foreach (var row in _rows.Where(x => !x.IsEnabled))
            {
                if (current.TryGetValue(row.Control.Setting.Property, out var control))
                {
                    row.Slider.Value = control.Setting.Value;
                }
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void DefaultsBtnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
        {
            if (row.Auto is not null)
            {
                row.Auto.IsChecked = true;
            }

            _isRefreshing = true;
            row.Slider.Value = row.Control.Default;
            _isRefreshing = false;

            Apply(row);
        }

        UpdateEnabledStates();
    }

    private void SaveBtnClick(object sender, RoutedEventArgs e)
    {
        _service.SaveSettings(_camera.Name, _rows.Select(x => x.Setting).ToArray());
        Close();
    }

    private void CancelBtnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _isClosed = true;
        _refreshTimer.Stop();
        _camera.Dispose();

        if (_preview is not null)
        {
            _ = _preview.DisposeAsync().AsTask();
            _preview = null;
        }

        // Without save, this puts the saved settings back
        _service.EndEditing();

        base.OnClosing(e);
    }
}
