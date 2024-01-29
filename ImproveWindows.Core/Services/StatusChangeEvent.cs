namespace ImproveWindows.Core.Services;

public class StatusChangeEventArgs : EventArgs, IEquatable<StatusChangeEventArgs>
{
    public bool Equals(StatusChangeEventArgs? other)
    {
        return IsError == other?.IsError
            && Status == other?.Status;
    }

    public override bool Equals(object? obj)
    {
        return obj is StatusChangeEventArgs other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsError, Status);
    }

    public static bool operator ==(StatusChangeEventArgs left, StatusChangeEventArgs right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(StatusChangeEventArgs left, StatusChangeEventArgs right)
    {
        return !left.Equals(right);
    }

    public required bool IsError { get; init; }
    public required string Status { get; init; }
}