using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>按编号查配置。技能、Buff、弹道都用这份表。</summary>
    public sealed class IdCatalog<T> where T : class
    {
        readonly Dictionary<int, T> _map = new Dictionary<int, T>();
        readonly Func<T, int> _idOf;
        readonly Action<T> _prepare;

        public IdCatalog(Func<T, int> idOf, Action<T> prepare = null)
        {
            _idOf = idOf ?? throw new ArgumentNullException(nameof(idOf));
            _prepare = prepare;
        }

        public IReadOnlyDictionary<int, T> All => _map;
        public int Count => _map.Count;
        public bool Contains(int id) => _map.ContainsKey(id);

        public void RegisterOrReplace(T data)
        {
            if (data == null) return;
            int id = _idOf(data);
            if (id == 0) return;
            _prepare?.Invoke(data);
            _map[id] = data;
        }

        public void ClearAndLoad(IEnumerable<T> list)
        {
            _map.Clear();
            if (list == null) return;
            foreach (var item in list)
                RegisterOrReplace(item);
        }

        public void Clear() => _map.Clear();

        public T Get(int id)
        {
            _map.TryGetValue(id, out var data);
            return data;
        }

        public bool TryGet(int id, out T data) => _map.TryGetValue(id, out data);

        public bool TryFind(Func<T, bool> match, out T data)
        {
            data = null;
            if (match == null) return false;
            foreach (var kv in _map)
            {
                if (kv.Value == null || !match(kv.Value)) continue;
                data = kv.Value;
                return true;
            }
            return false;
        }
    }
}
