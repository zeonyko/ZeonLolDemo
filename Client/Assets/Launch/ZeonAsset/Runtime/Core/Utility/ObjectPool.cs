using System;
using System.Collections.Generic;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 轻量对象池：复用 AsyncOperation 和 AssetHandle，减少 GC。
    /// </summary>
    public class ObjectPool<T> where T : class, new()
    {
        private readonly Stack<T> _stack = new Stack<T>(32);
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;

        public int CountInactive => _stack.Count;

        public ObjectPool(Action<T> onGet = null, Action<T> onRelease = null)
        {
            _onGet = onGet;
            _onRelease = onRelease;
        }

        public T Get()
        {
            var item = _stack.Count > 0 ? _stack.Pop() : new T();
            _onGet?.Invoke(item);
            return item;
        }

        public void Release(T item)
        {
            if (item == null)
                return;

            _onRelease?.Invoke(item);
            _stack.Push(item);
        }

        public void Clear()
        {
            _stack.Clear();
        }
    }
}
