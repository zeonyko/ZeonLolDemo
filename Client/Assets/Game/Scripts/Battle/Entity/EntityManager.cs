using System.Collections.Generic;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>实体名单：登记、查找、每帧推进。由游戏入口驱动。</summary>
    public class EntityManager
    {
        #region 全局一份与列表
        public static EntityManager Instance { get; private set; }

        private readonly Dictionary<long, Entity> _entities = new Dictionary<long, Entity>(); // 按 Id 查找
        private readonly List<Entity> _entityList = new List<Entity>(); // 按加入顺序每帧遍历
        #endregion

        #region 创建与销毁
        /// <summary>创建全局名单，开战时调一次。</summary>
        public static EntityManager Create()
        {
            Instance = new EntityManager();
            return Instance;
        }

        /// <summary>关闭：倒着销毁全部实体，避免遍历时被改乱。</summary>
        public void Shutdown()
        {
            for (int i = _entityList.Count - 1; i >= 0; i--)
                _entityList[i]?.Destroy();

            _entities.Clear();
            _entityList.Clear();
            Instance = null;
        }
        #endregion

        #region 注册与查询
        /// <summary>登记实体。同 Id 已有就忽略。</summary>
        public void RegisterEntity(Entity entity)
        {
            if (!_entities.ContainsKey(entity.Id))
            {
                _entities[entity.Id] = entity;
                _entityList.Add(entity);
            }
        }

        /// <summary>按 Id 找实体；没有返回 null。</summary>
        public Entity GetEntity(long id)
        {
            _entities.TryGetValue(id, out var entity);
            return entity;
        }

        /// <summary>当前全部实体（只读）。</summary>
        public IReadOnlyList<Entity> GetAllEntities() => _entityList;

        /// <summary>移除并销毁指定实体。</summary>
        public void RemoveEntity(long id)
        {
            if (_entities.TryGetValue(id, out var entity))
            {
                entity.Destroy();
                _entities.Remove(id);
                _entityList.Remove(entity);
            }
        }
        #endregion

        #region 每帧推进
        /// <summary>先采输入，再跑逻辑。单帧时间上限 0.1 秒，避免卡顿一步迈太大。</summary>
        public void Tick(float dt)
        {
            dt = Mathf.Min(dt, 0.1f);

            for (int i = 0; i < _entityList.Count; i++)
                _entityList[i]?.OnPreUpdate(dt);

            for (int i = 0; i < _entityList.Count; i++)
                _entityList[i]?.OnUpdate(dt);
        }

        /// <summary>画面收尾：镜头跟随等，放在逻辑之后。</summary>
        public void LateTick(float dt)
        {
            for (int i = 0; i < _entityList.Count; i++)
                _entityList[i]?.OnLateUpdate(dt);
        }
        #endregion
    }
}
