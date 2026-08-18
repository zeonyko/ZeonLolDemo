using System;
using System.Collections.Generic;

namespace Client.Battle
{
    /// <summary>战斗实体：挂组件、每帧推进，并在内部发事件。</summary>
    public class Entity
    {
        #region 基础身份
        public long Id { get; private set; }
        public string Name { get; set; }
        public bool IsLocalPlayer { get; set; }
        #endregion

        #region 组件存储
        private readonly Dictionary<Type, Component> _components = new Dictionary<Type, Component>(); // 同类型只挂一份
        private readonly List<Component> _updatableComponents = new List<Component>(); // 按添加顺序每帧推进
        #endregion

        #region 事件监听表
        // 实体内部事件
        private readonly Dictionary<string, Delegate> _eventListeners = new Dictionary<string, Delegate>();
        #endregion

        public Entity(long id)
        {
            Id = id;
        }

        #region 组件管理
        /// <summary>挂组件（同类型只留一份），马上走出生回调。</summary>
        public T AddComponent<T>() where T : Component, new()
        {
            var type = typeof(T);
            if (_components.ContainsKey(type))
                return _components[type] as T;

            var comp = new T { Owner = this };
            _components[type] = comp;
            _updatableComponents.Add(comp);
            comp.OnAwake();
            comp.OnStart();
            return comp;
        }

        /// <summary>按类型取组件；没挂返回 null。</summary>
        public T GetComponent<T>() where T : Component
        {
            if (_components.TryGetValue(typeof(T), out var comp))
                return comp as T;
            return null;
        }

        /// <summary>先跑各组件的帧前逻辑（采输入等）。</summary>
        public void OnPreUpdate(float dt)
        {
            for (int i = 0; i < _updatableComponents.Count; i++)
                if (_updatableComponents[i].Enabled) _updatableComponents[i].OnPreUpdate(dt);
        }

        /// <summary>再跑各组件的主逻辑。</summary>
        public void OnUpdate(float dt)
        {
            for (int i = 0; i < _updatableComponents.Count; i++)
                if (_updatableComponents[i].Enabled) _updatableComponents[i].OnUpdate(dt);
        }

        /// <summary>最后跑画面/镜头等收尾。</summary>
        public void OnLateUpdate(float dt)
        {
            for (int i = 0; i < _updatableComponents.Count; i++)
                if (_updatableComponents[i].Enabled) _updatableComponents[i].OnLateUpdate(dt);
        }
        #endregion

        #region 事件
        /// <summary>听某个事件，同名可挂多个处理。</summary>
        public void AddListener<T>(string eventName, Action<T> handler)
        {
            if (!_eventListeners.ContainsKey(eventName))
                _eventListeners[eventName] = null;
            _eventListeners[eventName] = (Action<T>)_eventListeners[eventName] + handler;
        }

        /// <summary>取消监听。</summary>
        public void RemoveListener<T>(string eventName, Action<T> handler)
        {
            if (_eventListeners.ContainsKey(eventName))
            {
                _eventListeners[eventName] = (Action<T>)_eventListeners[eventName] - handler;
            }
        }

        /// <summary>通知所有在听这个事件的人。</summary>
        public void DispatchEvent<T>(string eventName, T eventData)
        {
            if (_eventListeners.TryGetValue(eventName, out var del) && del is Action<T> callback)
            {
                callback?.Invoke(eventData);
            }
        }
        #endregion

        #region 生命周期
        /// <summary>销毁：先清状态，再通知各组件，最后清空。</summary>
        public void Destroy()
        {
            GetComponent<StateComponent>()?.ClearAll();

            foreach (var comp in _components.Values)
            {
                comp.OnDestroy();
            }
            _components.Clear();
            _updatableComponents.Clear();
            _eventListeners.Clear();
        }
        #endregion
    }
}