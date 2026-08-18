using UnityEngine;

namespace Client.Battle
{
    /// <summary>一次性世界特效：Prefab 自己位移、淡出、超时销毁。</summary>
    public class TransientFx : MonoBehaviour
    {
        #region 表现开关（由 Bind/With 系列方法设置）
        /// <summary>弹道等 persist 特效的硬上限，防止消失包丢失后一直挂着。</summary>
        public const float MaxPersistSeconds = 8f;

        public float Duration = 0.7f;
        public Vector3 WorldVelocity = new Vector3(0f, 1.6f, 0f);
        public bool FadeText;
        public bool FadeRenderer;
        public bool Shrink;
        public bool ExpandPulse;
        public bool Billboard;
        /// <summary>由调用方取消；到期仍会自毁（安全网）。</summary>
        public bool Persist { get; private set; }
        #endregion

        #region 运行时缓存字段
        private float _age;                    // 已存活时间
        private TextMesh _text;                // 飘字用文本组件（WithText 时创建）
        private Renderer _primary;              // 主渲染器（用于 Shrink / 单渲染器淡出）
        private Renderer[] _renderers;          // 全部子渲染器（用于批量淡出）
        private Color[] _baseColors;            // 各渲染器的初始颜色，淡出时按比例衰减 alpha
        private Color _baseColor = Color.white; // 飘字/主渲染器的初始颜色
        private Vector3 _baseScale = Vector3.one;     // 主渲染器初始缩放（Shrink 用）
        private Vector3 _rootBaseScale = Vector3.one; // 根节点初始缩放（ExpandPulse 用）
        private Transform _billboardTarget;     // 广告牌朝向目标（默认主渲染器 transform）
        private bool _countedForLod;            // 是否已计入 VfxLod 的存活计数，销毁时需对应减一
        private Transform _followTarget;        // Buff 罩子等：每帧跟随挂点
        private Vector3 _followWorldOffset;
        #endregion

        #region 创建 / 绑定
        public static TransientFx Spawn(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(BattleScene.Vfx, true);
            go.transform.position = pos;
            return go.AddComponent<TransientFx>();
        }

        /// <summary>绑定 Prefab 上的主渲染器与表现开关。</summary>
        public TransientFx BindPrefabVisual(
            Renderer renderer, bool fade, bool shrink, bool billboard, bool expandPulse = false)
        {
            _primary = renderer;
            FadeRenderer = fade;
            Shrink = shrink;
            ExpandPulse = expandPulse;
            Billboard = billboard;
            _rootBaseScale = transform.localScale;

            _renderers = GetComponentsInChildren<Renderer>(true);
            if (_renderers != null && _renderers.Length > 0)
            {
                _baseColors = new Color[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var r = _renderers[i];
                    if (r != null && r.material != null && r.material.HasProperty("_Color"))
                        _baseColors[i] = r.material.color;
                    else if (r != null && r.material != null && r.material.HasProperty("_TintColor"))
                        _baseColors[i] = r.material.GetColor("_TintColor");
                    else
                        _baseColors[i] = Color.white;
                }
            }

            if (renderer != null)
            {
                _billboardTarget = renderer.transform;
                _baseScale = renderer.transform.localScale;
                if (renderer.material != null && renderer.material.HasProperty("_Color"))
                    _baseColor = renderer.material.color;
            }
            else
            {
                _baseScale = transform.localScale;
            }
            return this;
        }

        public TransientFx WithText(string content, Color color, float charSize = 0.12f)
        {
            _text = gameObject.AddComponent<TextMesh>();
            _text.text = content;
            _text.color = color;
            _text.fontSize = 64;
            _text.characterSize = charSize;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _baseColor = color;
            FadeText = true;
            return this;
        }

        public TransientFx WithMotion(Vector3 velocity, float duration)
        {
            WorldVelocity = velocity;
            if (duration > MaxPersistSeconds) duration = MaxPersistSeconds;
            Duration = duration;
            return this;
        }

        public TransientFx MarkLodCounted()
        {
            _countedForLod = true;
            return this;
        }
        #endregion

        #region 生命周期
        public void Cancel()
        {
            if (this == null) return;
            StopParticles();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            StopParticles();
            if (_countedForLod)
            {
                _countedForLod = false;
                VfxLod.NotifyDespawned();
            }
        }

        public TransientFx PersistUntilCancel()
        {
            Persist = true;
            FadeText = false;
            FadeRenderer = false;
            Shrink = false;
            ExpandPulse = false;
            Duration = MaxPersistSeconds;
            WorldVelocity = Vector3.zero;
            return this;
        }

        /// <summary>每帧贴到目标 Transform（世界偏移），用于 Buff 循环罩子等。</summary>
        public TransientFx Follow(Transform target, Vector3 worldOffset = default)
        {
            _followTarget = target;
            _followWorldOffset = worldOffset;
            WorldVelocity = Vector3.zero;
            if (target != null)
                transform.position = target.position + worldOffset;
            return this;
        }

        /// <summary>重置存活计时（Buff 循环罩子每帧续命，避免 Persist 8 秒安全网上限误杀）。</summary>
        public void RefreshLifetime(float seconds = MaxPersistSeconds)
        {
            _age = 0f;
            if (seconds > 0.05f)
                Duration = seconds;
        }

        void StopParticles()
        {
            var particles = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null)
                    particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
        #endregion

        #region 帧更新（位移 / 淡出 / 缩放 / 广告牌）
        private void Update()
        {
            float dt = Time.deltaTime;
            _age += dt;
            float t = Duration > 0.0001f ? Mathf.Clamp01(_age / Duration) : 1f;
            float easeOut = 1f - (1f - t) * (1f - t);

            UpdateMotionAndTextFacing(dt);
            UpdateExpandPulse(easeOut);

            float alpha = 1f - t;
            // 前半段保持亮，后半段快速收
            float visualAlpha = t < 0.35f
                ? Mathf.Lerp(1f, 0.85f, t / 0.35f)
                : Mathf.Lerp(0.85f, 0f, (t - 0.35f) / 0.65f);

            UpdateTextFade(alpha);
            UpdateRendererFade(visualAlpha);
            UpdateShrink(easeOut);
            UpdateBillboard();

            if (_age >= Duration)
                Destroy(gameObject);
        }

        /// <summary>跟随挂点或沿世界速度位移；若挂了飘字，整体面向摄像机。</summary>
        private void UpdateMotionAndTextFacing(float dt)
        {
            if (_followTarget != null)
                transform.position = _followTarget.position + _followWorldOffset;
            else
                transform.position += WorldVelocity * dt;

            if (Camera.main != null && _text != null)
                transform.rotation = Camera.main.transform.rotation;
        }

        /// <summary>展开脉冲：根节点由小放大，再靠 alpha 淡出。</summary>
        private void UpdateExpandPulse(float easeOut)
        {
            if (!ExpandPulse) return;
            float pulse = Mathf.Lerp(0.55f, 1.35f, easeOut);
            transform.localScale = _rootBaseScale * pulse;
        }

        private void UpdateTextFade(float alpha)
        {
            if (!FadeText || _text == null) return;
            var c = _baseColor;
            c.a = alpha;
            _text.color = c;
        }

        private void UpdateRendererFade(float visualAlpha)
        {
            if (FadeRenderer && _renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    var r = _renderers[i];
                    if (r == null || r is ParticleSystemRenderer) continue;
                    if (r.material == null) continue;

                    var baseCol = _baseColors != null && i < _baseColors.Length
                        ? _baseColors[i]
                        : Color.white;
                    var c = baseCol;
                    c.a = baseCol.a * visualAlpha;

                    if (r.material.HasProperty("_Color"))
                        r.material.color = c;
                    if (r.material.HasProperty("_TintColor"))
                        r.material.SetColor("_TintColor", c);
                    if (r.material.HasProperty("_Alpha"))
                        r.material.SetFloat("_Alpha", c.a);
                }
            }
            else if (FadeRenderer && _primary != null
                     && _primary.material != null
                     && _primary.material.HasProperty("_Color"))
            {
                var c = _baseColor;
                c.a = _baseColor.a * visualAlpha;
                _primary.material.color = c;
            }
        }

        private void UpdateShrink(float easeOut)
        {
            if (Shrink && _primary != null && !ExpandPulse)
                _primary.transform.localScale = Vector3.Lerp(_baseScale, _baseScale * 0.2f, easeOut);
        }

        private void UpdateBillboard()
        {
            if (!Billboard || Camera.main == null) return;
            var target = _billboardTarget != null ? _billboardTarget : transform;
            Vector3 toCam = target.position - Camera.main.transform.position;
            if (toCam.sqrMagnitude > 0.0001f)
                target.rotation = Quaternion.LookRotation(toCam);
        }
        #endregion
    }
}
