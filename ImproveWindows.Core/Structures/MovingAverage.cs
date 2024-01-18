namespace ImproveWindows.Core.Structures;

public class MovingAverage16
{
    private readonly int[] _sum = new int[16];
    private int _currentPos;
    private bool _isFull;

    public void Add(int value)
    {
        _sum[_currentPos] = value;
        _isFull |= _currentPos == _sum.Length - 1;
        _currentPos = (_currentPos + 1) % _sum.Length;
    }
    
    public int GetAverage()
    {
        return (int) Math.Round(_sum.Sum() / (double) (_isFull ? _sum.Length : _currentPos));
    }
}