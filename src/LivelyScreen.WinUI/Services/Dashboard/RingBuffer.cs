namespace LivelyScreen.Services.Dashboard;

public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        if (_count < _buffer.Length)
        {
            int writeIndex = (_head + _count) % _buffer.Length;
            _buffer[writeIndex] = item;
            _count++;
        }
        else
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
        }
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _buffer[(_head + index) % _buffer.Length];
        }
    }

    public void CopyTo(Span<T> destination)
    {
        int toCopy = Math.Min(_count, destination.Length);
        for (int i = 0; i < toCopy; i++)
        {
            destination[i] = _buffer[(_head + i) % _buffer.Length];
        }
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _head = 0;
        _count = 0;
    }
}
