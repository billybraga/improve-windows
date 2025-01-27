using System.Globalization;

namespace ImproveWindows.Core.Structures;

public class MovingAverage16
{
    private readonly int[] _values = new int[16];
    private int _currentPos;
    private bool _isFull;

    public void Add(int value)
    {
        _values[_currentPos] = value;
        _isFull |= _currentPos == _values.Length - 1;
        _currentPos = (_currentPos + 1) % _values.Length;
    }

    public override string ToString()
    {
        return string.Join(", ", ToValues().Select(x => x.Value.ToString(NumberFormatInfo.InvariantInfo)));
    }

    public string ToStringWithTemplate()
    {
        return string.Join(", ", ToValues().Select(x => $"{x.Name}: {x.Value}"));
    }

    private (string Name, int Value)[] ToValues()
    {
        var values = _isFull ? _values : _values.Take(_currentPos + 1).ToArray();

        return
        [
            ("min", values.Min()),
            ("avg", (int) Math.Round(values.Sum() / (double) values.Length)),
            ("max", values.Max()),
        ];
    }
}