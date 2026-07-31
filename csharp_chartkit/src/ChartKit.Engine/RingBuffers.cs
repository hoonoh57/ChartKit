using System.Collections;
using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.Engine;

public sealed class CandleRingBuffer : IReadOnlyList<Candle>
{
    private readonly Candle[] _items;
    private int _head;
    private int _count;

    public CandleRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new Candle[capacity];
    }

    public int Capacity => _items.Length;
    public int Count => _count;
    public long FirstSequence => _count == 0 ? -1 : this[0].Sequence;
    public long LastSequence => _count == 0 ? -1 : this[_count - 1].Sequence;

    public Candle this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[PhysicalIndex(index)];
        }
    }

    public bool Add(Candle value)
    {
        bool evicted = _count == _items.Length;
        if (evicted)
        {
            _items[_head] = value;
            _head = (_head + 1) % _items.Length;
        }
        else
        {
            _items[PhysicalIndex(_count)] = value;
            _count++;
        }
        return evicted;
    }

    public void ReplaceLast(Candle value)
    {
        if (_count == 0)
        {
            Add(value);
            return;
        }
        _items[PhysicalIndex(_count - 1)] = value;
    }

    public Candle[] Snapshot()
    {
        var result = new Candle[_count];
        CopyTo(result);
        return result;
    }

    public void CopyTo(Span<Candle> destination)
    {
        if (destination.Length < _count) throw new ArgumentException("Destination is too small.", nameof(destination));
        for (int index = 0; index < _count; index++) destination[index] = this[index];
    }

    public void Clear()
    {
        Array.Clear(_items);
        _head = 0;
        _count = 0;
    }

    private int PhysicalIndex(int logicalIndex) => (_head + logicalIndex) % _items.Length;

    public IEnumerator<Candle> GetEnumerator()
    {
        for (int index = 0; index < _count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class IndicatorPointRingBuffer : IReadOnlyList<IndicatorPoint>
{
    private readonly IndicatorPoint[] _items;
    private int _head;
    private int _count;

    public IndicatorPointRingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = new IndicatorPoint[capacity];
    }

    public int Count => _count;

    public IndicatorPoint this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(_head + index) % _items.Length];
        }
    }

    public void AddOrReplace(IndicatorPoint point)
    {
        if (_count > 0 && this[_count - 1].Sequence == point.Sequence)
        {
            _items[(_head + _count - 1) % _items.Length] = point;
            return;
        }

        if (_count == _items.Length)
        {
            _items[_head] = point;
            _head = (_head + 1) % _items.Length;
        }
        else
        {
            _items[(_head + _count) % _items.Length] = point;
            _count++;
        }
    }

    public IndicatorPoint[] Snapshot()
    {
        var result = new IndicatorPoint[_count];
        for (int index = 0; index < _count; index++) result[index] = this[index];
        return result;
    }

    public IEnumerator<IndicatorPoint> GetEnumerator()
    {
        for (int index = 0; index < _count; index++) yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
