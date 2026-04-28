using System;
using System.Collections.Generic;

namespace MapLibre.Unity.Pool
{
    public class ObjectPool<T> where T : class
    {
        private readonly Stack<T> _available;
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly int _maxSize;
        private int _countActive;

        public ObjectPool(Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null,
            int initialSize = 0, int maxSize = 64)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onRelease = onRelease;
            _maxSize = maxSize;
            _available = new Stack<T>(Math.Max(initialSize, 8));

            for (int i = 0; i < initialSize; i++)
                _available.Push(_factory());
        }

        public T Get()
        {
            T item = _available.Count > 0 ? _available.Pop() : _factory();
            _onGet?.Invoke(item);
            _countActive++;
            return item;
        }

        public void Release(T item)
        {
            if (item == null) return;

            _onRelease?.Invoke(item);
            _countActive--;

            if (_available.Count < _maxSize)
                _available.Push(item);
        }

        public void Clear()
        {
            _available.Clear();
            _countActive = 0;
        }

        public int CountActive => _countActive;
        public int CountAvailable => _available.Count;
    }
}
