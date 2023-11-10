namespace ImproveWindows.Cli.Extensions;

public static class ComparableUtils
{
    public static T Min<T>(T item1, T item2)
        where T : IComparable
    {
        if (item1.CompareTo(item2) < 0)
        {
            return item1;
        }

        return item2;
    }
}