namespace Warden.Domain;

/// <summary>
/// Ring buffer de log em memória — estado vivo, não vai pro SQLite. Locking próprio porque, ao
/// contrário do Python (GIL protege o deque), aqui há concorrência real entre a thread que lê o
/// processo filho e as threads de request que servem `logs()`.
/// </summary>
public sealed class RingBuffer
{
    private readonly int _maxLen;
    private readonly LinkedList<string> _buffer = new();
    private readonly Lock _lock = new();

    public RingBuffer(int maxLen = 1000)
    {
        _maxLen = maxLen;
    }

    public void Append(string line)
    {
        lock (_lock)
        {
            _buffer.AddLast(line);
            while (_buffer.Count > _maxLen)
            {
                _buffer.RemoveFirst();
            }
        }
    }

    public IReadOnlyList<string> Tail(int n)
    {
        lock (_lock)
        {
            if (n >= _buffer.Count) return _buffer.ToList();
            return _buffer.Skip(_buffer.Count - n).ToList();
        }
    }
}
