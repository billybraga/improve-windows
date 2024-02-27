using FluentAssertions;
using ImproveWindows.Core.Structures;

namespace ImproveWindows.Tests;

public class MovingAverage16Tests
{
    [Fact]
    public void Test()
    {
        var movingAverage16 = new MovingAverage16();
        string[] expected =
        [
            "0, 0, 0",
            "0, 0, 1",
            "0, 1, 2",
            "0, 1, 3",
            "0, 2, 4",
            "0, 2, 5",
            "0, 3, 6",
            "0, 3, 7",
            "0, 4, 8",
            "0, 4, 9",
            "0, 5, 10",
            "0, 5, 11",
            "0, 6, 12",
            "0, 6, 13",
            "0, 7, 14",
            "0, 8, 15",
            "1, 8, 16",
            "2, 10, 17",
        ];
        for (var i = 0; i < 18; i++)
        {
            movingAverage16.Add(i);
            var average = movingAverage16.ToString();
            average
                .Should()
                .Be(expected[i], $"was not the right value it i={i}");
        }
    }
}