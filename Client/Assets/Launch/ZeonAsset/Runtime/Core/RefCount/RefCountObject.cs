using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 引用计数抽象基类。归零时触发 OnRefZero（由子类决定立刻卸载或进延迟队列）。
    /// </summary>
    public abstract class RefCountObject : IRefable
    {
        public int RefCount { get; private set; }

        public virtual void Retain()
        {
            RefCount++;
            OnRefCountChanged();
        }

        public virtual void Release()
        {
            if (RefCount <= 0)
            {
                ZeonAssetLog.Warn($"Release called with RefCount<=0 on {GetType().Name}");
                return;
            }

            RefCount--;
            OnRefCountChanged();

            if (RefCount == 0)
                OnRefZero();
        }

        protected virtual void OnRefCountChanged()
        {
        }

        /// <summary>引用归零回调。</summary>
        protected abstract void OnRefZero();

        protected void ResetRefCount()
        {
            RefCount = 0;
        }

        /// <summary>强制归零并触发 OnRefZero（用于 Destroy / 强制卸载）。</summary>
        protected void ForceZero()
        {
            if (RefCount == 0)
            {
                OnRefZero();
                return;
            }

            RefCount = 0;
            OnRefCountChanged();
            OnRefZero();
        }
    }
}
