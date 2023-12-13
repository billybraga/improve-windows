namespace ImproveWindows.Core.Services;

public struct StatusEventArgs
{
    public required bool IsError { get; init; }
    public required string Status { get; init; }
}