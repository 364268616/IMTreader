using System.Windows.Media.Imaging;

namespace IMTReader.Core.Imaging;

/// <summary>按 (页码, DPI) 缓存已渲染页面的 LRU 缓存；线程安全。</summary>
public sealed class PageCache
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly Dictionary<(int, int), LinkedListNode<((int, int) Key, BitmapSource Image)>> _map = new();
    private readonly LinkedList<((int, int) Key, BitmapSource Image)> _lru = new();

    public PageCache(int capacity = 16)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(int page, int dpi, out BitmapSource image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((page, dpi), out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                image = node.Value.Image;
                return true;
            }
        }
        image = null!;
        return false;
    }

    public void Put(int page, int dpi, BitmapSource image)
    {
        lock (_gate)
        {
            if (_map.TryGetValue((page, dpi), out var existing))
            {
                _lru.Remove(existing);
                _map.Remove((page, dpi));
            }
            var node = _lru.AddFirst(((page, dpi), image));
            _map[(page, dpi)] = node;
            while (_lru.Count > _capacity)
            {
                var last = _lru.Last!;
                _map.Remove(last.Value.Key);
                _lru.RemoveLast();
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }
}
