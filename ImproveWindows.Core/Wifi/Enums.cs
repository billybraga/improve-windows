namespace ImproveWindows.Core.Wifi;

/// <summary>
/// The DOT11_AUTH_ALGORITHM enumerated type defines a wireless LAN authentication algorithm.
/// </summary>
public enum Dot11AuthAlgorithm : uint
{
    Open = 1,
    SharedKey = 2,
    Wpa = 3,
    WpaPsk = 4,
    WpaNone = 5,
    Rsna = 6,
    RsnaPsk = 7,
    IhvStart = 0x80000000,
    IhvEnd = 0xffffffff
}

/// <summary>
/// The DOT11_BSS_TYPE enumerated type defines a basic service set (BSS) network type.
/// </summary>
public enum Dot11BssType
{
    Infrastructure = 1,
    Independent = 2,
    Any = 3
}

public enum Dot11CipherAlgorithm : uint
{
    None = 0x00,
    Wep40 = 0x01,
    Tkip = 0x02,
    Ccmp = 0x04,
    Wep104 = 0x05,
    WpaUseGroup = 0x100,
    Wep = 0x101,
    IhvStart = 0x80000000,
    IhvEnd = 0xffffffff
}

public enum Dot11PhyType : uint
{
    Unknown = 0,
    Fhss = 1,
    Dsss = 2,
    IrBaseBand = 3,
    Ofdm = 4,
    Hrdsss = 5,
    Erp = 6,
    Ht = 7,
    He = 10,
    IhvStart = 0x80000000,
    IhvEnd = 0xffffffff
}

public enum WlanConnectionMode
{
    Profile,
    TemporaryProfile,
    DiscoverySecure,
    DiscoveryUnsecure,
    Auto,
    Invalid
}

public enum WlanHostedNetworkPeerAuthState
{
    Invalid,
    Authenticated
}

public enum WlanHostedNetworkReason
{
    Success = 0,
    Unspecified,
    BadParameters,
    ServiceShuttingDown,
    InsufficientResources,
    ElevationRequired,
    ReadOnly,
    PersistenceFailed,
    CryptError,
    Impersonation,
    StopBeforeStart,
    InterfaceAvailable,
    InterfaceUnavailable,
    MiniportStopped,
    MiniportStarted,
    IncompatibleConnectionStarted,
    IncompatibleConnectionStopped,
    UserAction,
    ClientAbort,
    ApStartFailed,
    PeerArrived,
    PeerDeparted,
    PeerTimeout,
    GpDenied,
    ServiceUnavailable,
    DeviceChange,
    PropertiesChange,
    VirtualStationBlockingUse,
    ServiceAvailableOnVirtualStation
}

public enum WlanInterfaceState
{
    NotReady = 0,
    Connected = 1,
    AdhocNetworkFormed = 2,
    Disconnecting = 3,
    Disconnected = 4,
    Associating = 5,
    Discovering = 6,
    Authenticating = 7
}

public enum WlanIntfOpcode
{
    AutoconfStart = 0x000000000,
    AutoconfEnabled,
    BackgroundScanEnabled,
    MediaStreamingMode,
    RadioState,
    BssType,
    InterfaceState,
    CurrentConnection,
    ChannelNumber,
    SupportedInfrastructureAuthCipherPairs,
    SupportedAdhocAuthCipherPairs,
    SupportedCountryOrRegionStringList,
    CurrentOperationMode,
    SupportedSafeMode,
    CertifiedSafeMode,
    HostedNetworkCapable,
    ManagementFrameProtectionCapable,
    AutoconfEnd = 0x0fffffff,
    MsmStart = 0x10000100,
    Statistics,
    Rssi,
    MsmEnd = 0x1fffffff,
    SecurityStart = 0x20010000,
    SecurityEnd = 0x2fffffff,
    IhvStart = 0x30000000,
    IhvEnd = 0x3fffffff
}

public enum WlanOpcodeValueType
{
    QueryOnly = 0,
    SetByGroupPolicy = 1,
    SetByUser = 2,
    Invalid = 3
}