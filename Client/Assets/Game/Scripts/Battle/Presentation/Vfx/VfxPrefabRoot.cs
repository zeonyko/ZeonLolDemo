using UnityEngine;

namespace Client.Battle
{
    /// <summary>Prefab 上的特效根：存活时间、始终朝镜头、淡出，给一次性特效读。</summary>
    public sealed class VfxPrefabRoot : MonoBehaviour
    {
        [Tooltip("一次性特效默认活几秒；一直跟着飞的弹道不管这个")]
        public float DefaultDuration = 0.45f;

        [Tooltip("主模型始终朝向镜头（刀光面片）")]
        public bool Billboard = true;

        public bool FadeOut = true;
        public bool Shrink;

        [Tooltip("根节点由小放大再淡出（冲击环 / 刀光展开）")]
        public bool ExpandPulse;

        [Tooltip("没指定时自动找第一个子渲染器")]
        public Renderer PrimaryRenderer;

        /// <summary>取主渲染器。没指定就找第一个子物体上的。</summary>
        public Renderer ResolveRenderer()
        {
            if (PrimaryRenderer != null) return PrimaryRenderer;
            PrimaryRenderer = GetComponentInChildren<Renderer>();
            return PrimaryRenderer;
        }
    }
}
