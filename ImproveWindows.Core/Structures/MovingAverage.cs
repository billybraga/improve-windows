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
        var values = _isFull ? _values : _values.Take(_currentPos + 1).ToArray();

        double[] keys = [values.Min(), values.Sum() / (double) values.Length, values.Max()];
        return string.Join(", ", keys.Select(x => (int) Math.Round(x)));
    }
}