namespace ImproveWindows.Core.Services;

public class TextMessageEventArgs : EventArgs, IEquatable<TextMessageEventArgs>
{
    public bool Equals(TextMessageEventArgs? other)
    {
        return Message == other?.Message;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextMessageEventArgs other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Message.GetHashCode();
    }

    public static bool operator ==(TextMessageEventArgs left, TextMessageEventArgs right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextMessageEventArgs left, TextMessageEventArgs right)
    {
        return !left.Equals(right);
    }

    public required string Message { get; init; }
}