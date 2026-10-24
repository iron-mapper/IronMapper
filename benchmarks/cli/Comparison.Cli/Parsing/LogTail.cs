using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Comparison.Cli.Parsing;

/// <summary>
/// Follows a log file that another process writes (pwsh <c>*&gt; $log</c>): reads the lines added since the last poll.
/// <list type="bullet">
///   <item>The file may not exist yet, or may be replaced: it is opened anew on every poll, shared for read, write
///   and delete.</item>
///   <item>A log left by an earlier run is ignored until it is written again: content counts only when the file's last
///   write time is not earlier than <c>notBefore</c>.</item>
///   <item>A file that got shorter was rewritten: it is read again from the start.</item>
///   <item>Size and time come from the open handle. A directory listing (Get-ChildItem -Recurse, FindFirstFile) on
///   Windows reports the size stored in the directory entry, which stays 0 while the writer keeps the file open.</item>
/// </list>
/// The log is UTF-8 (pwsh 7 writes redirections without BOM); a trailing incomplete line waits for its end.
/// </summary>
internal sealed class LogTail
{
    private readonly DateTime _notBeforeUtc;
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder _partial = new();
    private long _position;

    public LogTail(string path, DateTime notBeforeUtc)
    {
        Path = path;
        _notBeforeUtc = notBeforeUtc;
    }

    public string Path { get; }

    /// <summary>When the file last got new content (UTC), or null if nothing was read yet.</summary>
    public DateTime? LastGrowthUtc { get; private set; }

    public bool Exists { get; private set; }

    /// <summary>The complete lines added since the previous call.</summary>
    public IReadOnlyList<string> Poll()
    {
        var lines = new List<string>();
        SafeFileHandle handle;
        try
        {
            handle = File.OpenHandle(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Exists = File.Exists(Path);
            return lines;
        }

        using (handle)
        {
            Exists = true;
            // Truncation to a few bytes counts as "older" only by time; a stale file is skipped entirely.
            if (File.GetLastWriteTimeUtc(handle) < _notBeforeUtc)
                return lines;
            long length = RandomAccess.GetLength(handle);
            if (length < _position)
            {
                _position = 0;
                _partial.Clear();
                _decoder.Reset();
            }
            if (length == _position)
                return lines;

            byte[] buffer = new byte[Math.Min(length - _position, 1 << 20)];
            char[] chars = new char[buffer.Length + 1];
            while (_position < length)
            {
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(buffer.Length, length - _position)), _position);
                if (read <= 0)
                    break;
                int start = _position == 0 && read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
                _position += read;
                int count = _decoder.GetChars(buffer, start, read - start, chars, 0);
                Split(chars.AsSpan(0, count), lines);
            }
            LastGrowthUtc = DateTime.UtcNow;
        }
        return lines;
    }

    private void Split(ReadOnlySpan<char> text, List<string> lines)
    {
        foreach (char c in text)
        {
            if (c == '\n')
            {
                int end = _partial.Length > 0 && _partial[^1] == '\r' ? _partial.Length - 1 : _partial.Length;
                lines.Add(_partial.ToString(0, end));
                _partial.Clear();
            }
            else
            {
                _partial.Append(c);
            }
        }
    }
}
