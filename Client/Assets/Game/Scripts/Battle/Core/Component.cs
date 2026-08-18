namespace Client.Battle
{
    /// <summary>组件基类：出生、每帧、销毁。</summary>
    public abstract class Component
    {
        public Entity Owner { internal set; get; }
        public bool Enabled { get; set; } = true;

        public virtual void OnAwake() { }
        public virtual void OnStart() { }
        
        // 一帧分三段
        public virtual void OnPreUpdate(float dt) { }  // 先采输入
        public virtual void OnUpdate(float dt) { }     // 再跑逻辑
        public virtual void OnLateUpdate(float dt) { } // 最后跟画面/镜头

        public virtual void OnDestroy() { }
    }
}