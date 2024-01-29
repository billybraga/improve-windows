namespace ImproveWindows.Core.Wifi;

[Flags]
public enum WlanNotificationSource : uint
{
    None = 0,
    OneX = 0X00000004,
    Acm = 0X00000008,
    Msm = 0X00000010,
    Security = 0X00000020,
    Ihv = 0X00000040,
    HostedNetwork = 0X00000080,
    All = 0X0000FFFF
}