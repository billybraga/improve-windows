using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace ImproveWindows.Ui.Webcam;

/// <summary>
/// Reads and writes UVC image settings through the DirectShow IAMVideoProcAmp and IAMCameraControl interfaces.
/// </summary>
internal sealed class UvcCamera : IDisposable
{
    public const string ProcAmpKind = "ProcAmp";
    public const string CameraControlKind = "CameraControl";

    private const int AutoFlag = 1;
    private const int ManualFlag = 2;

    private static readonly Guid SystemDeviceEnumClsid = new("62BE5D10-60EB-11d0-BD3B-00A0C911CE86");
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11d0-BD3B-00A0C911CE86");
    private static readonly Guid BaseFilterIid = new("56a86895-0ad4-11ce-b03a-0020af0ba770");

    // Indexes are the VideoProcAmpProperty and CameraControlProperty enum values
    private static readonly string[] ProcAmpNames =
    [
        "Brightness", "Contrast", "Hue", "Saturation", "Sharpness", "Gamma", "ColorEnable", "WhiteBalance", "BacklightCompensation", "Gain",
    ];

    private static readonly string[] CameraControlNames = ["Pan", "Tilt", "Roll", "Zoom", "Exposure", "Iris", "Focus"];

    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid clsidDeviceClass, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string propName, [MarshalAs(UnmanagedType.Struct)] out object? value, IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string propName, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }

    [ComImport, Guid("C6E13360-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMVideoProcAmp
    {
        [PreserveSig]
        int GetRange(int property, out int min, out int max, out int steppingDelta, out int defaultValue, out int capsFlags);

        [PreserveSig]
        int Set(int property, int value, int flags);

        [PreserveSig]
        int Get(int property, out int value, out int flags);
    }

    [ComImport, Guid("C6E13370-30AC-11d0-A18C-00A0C9118956"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMCameraControl
    {
        [PreserveSig]
        int GetRange(int property, out int min, out int max, out int steppingDelta, out int defaultValue, out int capsFlags);

        [PreserveSig]
        int Set(int property, int value, int flags);

        [PreserveSig]
        int Get(int property, out int value, out int flags);
    }

    private object? _filter;

    public string Name { get; }

    private UvcCamera(object filter, string name)
    {
        _filter = filter;
        Name = name;
    }

    /// <summary>
    /// Returns the current settings of the first camera whose name contains <paramref name="nameMatch"/>, or null if it isn't connected.
    /// </summary>
    public static (string CameraName, IReadOnlyList<WebcamSetting> Settings)? Read(string nameMatch)
    {
        using var camera = Open(nameMatch);
        return camera is null
            ? null
            : (camera.Name, camera.GetControls().Select(x => x.Setting).ToArray());
    }

    /// <summary>
    /// Writes the settings to the camera. Returns the HRESULT of each write, or null if the camera isn't connected.
    /// </summary>
    public static IReadOnlyList<(WebcamSetting Setting, int Result)>? Write(string nameMatch, IReadOnlyCollection<WebcamSetting> settings)
    {
        using var camera = Open(nameMatch);
        return camera is null
            ? null
            : settings.Select(x => (x, camera.Set(x))).ToArray();
    }

    /// <summary>
    /// Keeps the first camera whose name contains <paramref name="nameMatch"/> open until disposed, or returns null if it isn't connected.
    /// </summary>
    public static UvcCamera? Open(string nameMatch)
    {
        var deviceEnumType = Type.GetTypeFromCLSID(SystemDeviceEnumClsid, throwOnError: true)!;
        var deviceEnum = (ICreateDevEnum) Activator.CreateInstance(deviceEnumType)!;

        try
        {
            var category = VideoInputDeviceCategory;

            // S_FALSE with no enumerator when there's no video device at all
            if (deviceEnum.CreateClassEnumerator(ref category, out var enumMoniker, 0) != 0 || enumMoniker is null)
            {
                return null;
            }

            try
            {
                var monikers = new IMoniker[1];
                while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
                {
                    var moniker = monikers[0];
                    try
                    {
                        var propertyBagIid = typeof(IPropertyBag).GUID;
                        moniker.BindToStorage(null!, null, ref propertyBagIid, out var propertyBagObject);
                        var propertyBag = (IPropertyBag) propertyBagObject;

                        string? name;
                        try
                        {
                            _ = propertyBag.Read("FriendlyName", out var nameValue, IntPtr.Zero);
                            name = nameValue as string;
                        }
                        finally
                        {
                            _ = Marshal.ReleaseComObject(propertyBag);
                        }

                        if (name is null || !name.Contains(nameMatch, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var baseFilterIid = BaseFilterIid;
                        moniker.BindToObject(null!, null, ref baseFilterIid, out var filter);
                        return new UvcCamera(filter, name);
                    }
                    finally
                    {
                        _ = Marshal.ReleaseComObject(moniker);
                    }
                }

                return null;
            }
            finally
            {
                _ = Marshal.ReleaseComObject(enumMoniker);
            }
        }
        finally
        {
            _ = Marshal.ReleaseComObject(deviceEnum);
        }
    }

    public IReadOnlyList<WebcamControl> GetControls()
    {
        ObjectDisposedException.ThrowIf(_filter is null, this);

        var controls = new List<WebcamControl>();

        if (_filter is IAMVideoProcAmp procAmp)
        {
            for (var i = 0; i < ProcAmpNames.Length; i++)
            {
                if (procAmp.GetRange(i, out var min, out var max, out var step, out var defaultValue, out var caps) == 0
                    && procAmp.Get(i, out var value, out var flags) == 0)
                {
                    controls.Add(
                        new WebcamControl(
                            new WebcamSetting(ProcAmpKind, ProcAmpNames[i], value, (flags & AutoFlag) != 0),
                            min, max, step, defaultValue, (caps & AutoFlag) != 0
                        )
                    );
                }
            }
        }

        if (_filter is IAMCameraControl cameraControl)
        {
            for (var i = 0; i < CameraControlNames.Length; i++)
            {
                if (cameraControl.GetRange(i, out var min, out var max, out var step, out var defaultValue, out var caps) == 0
                    && cameraControl.Get(i, out var value, out var flags) == 0)
                {
                    controls.Add(
                        new WebcamControl(
                            new WebcamSetting(CameraControlKind, CameraControlNames[i], value, (flags & AutoFlag) != 0),
                            min, max, step, defaultValue, (caps & AutoFlag) != 0
                        )
                    );
                }
            }
        }

        return controls;
    }

    /// <summary>
    /// Returns the HRESULT of the write.
    /// </summary>
    public int Set(WebcamSetting setting)
    {
        ObjectDisposedException.ThrowIf(_filter is null, this);

        var flags = setting.Auto ? AutoFlag : ManualFlag;
        return setting.Kind switch
        {
            ProcAmpKind when _filter is IAMVideoProcAmp procAmp
                => procAmp.Set(Array.IndexOf(ProcAmpNames, setting.Property), setting.Value, flags),
            CameraControlKind when _filter is IAMCameraControl cameraControl
                => cameraControl.Set(Array.IndexOf(CameraControlNames, setting.Property), setting.Value, flags),
            _ => unchecked((int) 0x80004002), // E_NOINTERFACE
        };
    }

    public void Dispose()
    {
        if (_filter is not null)
        {
            _ = Marshal.ReleaseComObject(_filter);
            _filter = null;
        }
    }
}
