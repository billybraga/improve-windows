using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace ImproveWindows.Ui.Webcam;

/// <summary>
/// Streams the camera at its full resolution into a WPF bitmap.
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
            // Exclusive control is needed to pick the full resolution (1080p); it fails when another app (Teams) has the camera
            await mediaCapture.InitializeAsync(
                new MediaCaptureInitializationSettings
                {
                    SourceGroup = group,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                    MemoryPreference = MediaCaptureMemoryPreference.Cpu,
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                }
            );

            var source = mediaCapture.FrameSources.Values.First(x => x.Info.SourceKind == MediaFrameSourceKind.Color);
            var largestFormat = source.SupportedFormats
                .OrderByDescending(x => x.VideoFormat.Width * x.VideoFormat.Height)
                .ThenByDescending(x => (double) x.FrameRate.Numerator / x.FrameRate.Denominator)
                .First();
            await source.SetFormatAsync(largestFormat);

            // Not scaled down here: asking the reader for a smaller size costs more CPU than copying the full frame
            var frameReader = await mediaCapture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Bgra8);
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
        var softwareBitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (softwareBitmap is null || softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            _isRendering = 0;
            return;
        }

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
