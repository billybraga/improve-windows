using FluentAssertions;
using ImproveWindows.Core.Structures;

namespace ImproveWindows.Tests;

public class MovingAverage16Tests
{
    [Fact]
    public void Test()
    {
        var movingAverage16 = new MovingAverage16();
        var currentItems = new List<int>();
        for (var i = 0; i < 250; i++)
        {
            currentItems.Add(i);
            movingAverage16.Add(i);
            var expected = (int) Math.Round(currentItems.TakeLast(16).Average());
            var average = movingAverage16.GetAverage();
            average
                .Should()
                .Be(expected, $"was not the right value it i={i}");
        }
    }
}