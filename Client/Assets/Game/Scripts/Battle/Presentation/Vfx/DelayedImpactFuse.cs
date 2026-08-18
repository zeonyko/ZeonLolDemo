using UnityEngine;

namespace Client.Battle
{
    /// <summary>命中点引信：火球先留着跳一下，到点再播爆炸。</summary>
    public sealed class DelayedImpactFuse : MonoBehaviour
    {
        #region 状态字段
        private float _delay = 1f;                              // 引信延迟秒数
        private float _age;                                     // 已存活时间
        private float _radius = 2f;                             // 最终爆炸半径
        private Color _color = new Color(1f, 0.4f, 0.1f, 1f);   // 火球脉冲颜色
        private TransientFx _orb;                                // 引信期间显示的火球特效
        private TransientFx _telegraph;                          // 引信期间贴地预警环
        private Vector3 _baseScale = Vector3.one;                // 火球初始缩放，脉冲按此缩放
        #endregion

        #region 绑定
        public void Bind(Vector3 worldPos, float radius, float delay, TransientFx reuseOrb, Color color)
        {
            transform.position = worldPos;
            _radius = radius > 0.1f ? radius : 2f;
            _delay = delay > 0.05f ? delay : 0.8f;
            _color = color;
            _age = 0f;

            if (reuseOrb != null)
            {
                _orb = reuseOrb;
                _orb.transform.SetParent(null, true);
                _orb.transform.position = worldPos + Vector3.up * 0.9f;
                _orb.WorldVelocity = Vector3.zero;
                _orb.PersistUntilCancel();
                _orb.FadeRenderer = false;
                _orb.Shrink = false;
            }
            else
            {
                float s = Mathf.Clamp(_radius * 0.28f, 0.5f, 1.1f);
                _orb = VfxLibrary.SpawnTransient(
                    "Vfx_FuseOrb", worldPos + Vector3.up * 0.9f, Vector3.forward,
                    persist: true,
                    tint: _color,
                    scaleMul: Vector3.one * s,
                    onSpawned: fx =>
                    {
                        if (this == null) return;
                        if (_orb == null)
                            _orb = fx;
                        if (_orb != null)
                            _baseScale = _orb.transform.localScale;
                    });
                if (_orb != null)
                    _baseScale = _orb.transform.localScale;
            }

            if (_orb != null)
                _baseScale = _orb.transform.localScale;

            _telegraph = VfxManager.Instance?.PlayByVfxKey(
                "Vfx_SlowRing", worldPos, Vector3.forward, _radius, _delay, Vector3.one * _radius);
        }
        #endregion

        #region 帧更新与销毁
        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = _delay > 0.0001f ? Mathf.Clamp01(_age / _delay) : 1f;

            if (_orb != null)
            {
                float pulse = 1f + 0.25f * Mathf.Sin(_age * 14f) + 0.35f * t;
                _orb.transform.position = transform.position + Vector3.up * (0.9f + 0.08f * Mathf.Sin(_age * 10f));
                _orb.transform.localScale = _baseScale * pulse;
            }

            if (_age < _delay) return;

            if (_telegraph != null)
            {
                _telegraph.Cancel();
                _telegraph = null;
            }

            VfxManager.Instance?.PlayExplodeRing(transform.position, _radius);

            if (_orb != null)
            {
                _orb.Cancel();
                _orb = null;
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_orb != null)
            {
                _orb.Cancel();
                _orb = null;
            }
            if (_telegraph != null)
            {
                _telegraph.Cancel();
                _telegraph = null;
            }
        }
        #endregion
    }
}
