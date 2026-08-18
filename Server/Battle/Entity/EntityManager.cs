using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>本场景里的单位列表。</summary>
    public class EntityManager
    {
        private readonly Dictionary<long, Entity> _entities = new Dictionary<long, Entity>();
        private readonly Scene _scene;

        public EntityManager(Scene scene)
        {
            _scene = scene;
        }

        public int Count => _entities.Count;

        public void Add(Entity entity)
        {
            if (entity == null) return;
            _entities[entity.Id] = entity;
            entity.Scene = _scene;
        }

        public bool Remove(long entityId) => _entities.Remove(entityId);

        public Entity Get(long entityId)
        {
            _entities.TryGetValue(entityId, out var entity);
            return entity;
        }

        public IEnumerable<Entity> GetAll() => _entities.Values;

        public List<EntityData> GetAllData()
        {
            var list = new List<EntityData>(_entities.Count);
            foreach (var e in _entities.Values)
                list.Add(e.ToEntityData());
            return list;
        }

        public void CollectAiUnitsAlive(List<Entity> buffer)
        {
            buffer.Clear();
            foreach (var e in _entities.Values)
            {
                if (e.NeedsAiTick && e.Hp > 0f)
                    buffer.Add(e);
            }
        }
    }
}
