using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;

namespace ImproveWindows.Ui.Webcam;

/// <summary>
/// Streams the camera into a WPF bitmap. Opened in shared read-only mode so it works while another app (Teams) uses the camera.
/// </summary>
internal sealed class WebcamPreview : IAsyncDisposable
{
    private readonly MediaCapture _mediaCapture;
    private readonly MediaFrameReader _frameReader;
    private readonly Dispatcher _dispatcher;
    private readonly Action<ImageSource> _onBitmapCreated;
    private WriteableBitmap? _bitmap;
    private byte[] _pixels = [];
    private int _isRendering;

    private WebcamPreview(MediaCapture mediaCapture, MediaFrameReader frameReader, Dispatcher dispatcher, Action<ImageSource> onBitmapCreated)
    {
        _mediaCapture = mediaCapture;
        _frameReader = frameReader;
        _dispatcher = dispatcher;
        _onBitmapCreated = onBitmapCreated;
    }

    /// <summary>
    /// Returns null if no camera's name contains <paramref name="nameMatch"/>.
    /// </summary>
    public static async Task<WebcamPreview?> StartAsync(string nameMatch, Dispatcher dispatcher, Action<ImageSource> onBitmapCreated)
    {
        var groups = await MediaFrameSourceGroup.FindAllAsync();
        var group = groups.FirstOrDefault(
            x => x.DisplayName.Contains(nameMatch, StringComparison.OrdinalIgnoreCase)
                && x.SourceInfos.Any(s => s.SourceKind == MediaFrameSourceKind.Color)
        );

        if (group is null)
        {
            return null;
        }

        var mediaCapture = new MediaCapture();
        try
        {
            await mediaCapture.InitializeAsync(
                new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                }
            );

            var source = mediaCapture.FrameSources.Values.First(x => x.Info.SourceKind == MediaFrameSourceKind.Color);
            // Shared mode can't ask for Bgra8 conversion, so frames come in the camera's format and are converted in OnFrameArrived
            var frameReader = await mediaCapture.CreateFrameReaderAsync(source);
            frameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            var preview = new WebcamPreview(mediaCapture, frameReader, dispatcher, onBitmapCreated);
            frameReader.FrameArrived += preview.OnFrameArrived;

            var status = await frameReader.StartAsync();
            if (status != MediaFrameReaderStartStatus.Success)
            {
                throw new InvalidOperationException($"Preview failed to start: {status}");
            }

            return preview;
        }
        catch
        {
            mediaCapture.Dispose();
            throw;
        }
    }

    private void OnFrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        // Drop frames while the UI is still drawing the previous one
        if (Interlocked.Exchange(ref _isRendering, 1) == 1)
        {
            return;
        }

        using var frame = sender.TryAcquireLatestFrame();
        var sourceBitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (sourceBitmap is null)
        {
            _isRendering = 0;
            return;
        }

        using var softwareBitmap = sourceBitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
            ? SoftwareBitmap.Copy(sourceBitmap)
            : SoftwareBitmap.Convert(sourceBitmap, BitmapPixelFormat.Bgra8);

        var width = softwareBitmap.PixelWidth;
        var height = softwareBitmap.PixelHeight;
        var size = width * height * 4;
        if (_pixels.Length != size)
        {
            _pixels = new byte[size];
        }

        softwareBitmap.CopyToBuffer(_pixels.AsBuffer());

        _ = _dispatcher.InvokeAsync(
            () =>
            {
                try
                {
                    if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
                    {
                        _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                        _onBitmapCreated(_bitmap);
                    }

                    _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
                }
                finally
                {
                    _isRendering = 0;
                }
            },
            DispatcherPriority.Render
        );
    }

    public async ValueTask DisposeAsync()
    {
        _frameReader.FrameArrived -= OnFrameArrived;
        await _frameReader.StopAsync();
        _frameReader.Dispose();
        _mediaCapture.Dispose();
    }
}
