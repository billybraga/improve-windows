using NAudio.Wave;

namespace ImproveWindows.Ui.Audio;

/// <summary>
/// Stream for looping playback
/// Credit: http://mark-dot-net.blogspot.sg/2009/10/looped-playback-in-net-with-naudio.html
/// </summary>
public class LoopStream : WaveStream
{
    private readonly WaveStream _sourceStream;

    /// <summary>
    /// Creates a new Loop stream
    /// </summary>
    /// <param name="sourceStream">The stream to read from. Note: the Read method of this stream should return 0 when it reaches the end
    /// or else we will not loop to the start again.</param>
    public LoopStream(WaveStream sourceStream)
    {
        _sourceStream = sourceStream;
    }

    /// <summary>
    /// Return source stream's wave format
    /// </summary>
    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;

    /// <summary>
    /// LoopStream simply returns
    /// </summary>
    public override long Length => _sourceStream.Length;

    /// <summary>
    /// LoopStream simply passes on positioning to source stream
    /// </summary>
    public override long Position
    {
        get => _sourceStream.Position;
        set => _sourceStream.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var totalBytesRead = 0;

        while (totalBytesRead < count)
        {
            var bytesRead = _sourceStream.Read(buffer, offset + totalBytesRead, count - totalBytesRead);
            if (bytesRead == 0)
            {
                if (_sourceStream.Position == 0)
                {
                    // something wrong with the source stream
                    break;
                }

                // loop
                _sourceStream.Position = 0;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }
}