using UnityEngine;

namespace Client.Battle
{
    /// <summary>一条飘字：弹出放大 → 往上飘 → 淡出。</summary>
    public sealed class CombatTextItem : MonoBehaviour
    {
        #region 字段
        TextMesh _mesh;
        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        Color _fill = Color.white;
        Color _outline = Color.black;
        float _lifetime = 0.6f;
        float _pop = 0.08f;
        float _arc = 0.35f;
        float _scalePeak = 1.35f;
        float _age;
        Vector3 _spawnPos;
        Vector3 _arcDir;
        float _baseCharSize = 0.062f;
        bool _active;
        System.Action<CombatTextItem> _onDone;

        public bool IsActive => _active;
        #endregion

        #region 播放控制
        public void Bind(TextMesh mesh, MeshRenderer renderer)
        {
            _mesh = mesh;
            _renderer = renderer;
            _mpb = new MaterialPropertyBlock();
        }

        public void Play(
            string content,
            CombatTextStyleEntry style,
            Color? overrideColor,
            System.Action<CombatTextItem> onDone)
        {
            if (_mesh == null || style == null) return;

            _onDone = onDone;
            _fill = overrideColor ?? style.FillColor;
            _outline = style.OutlineColor;
            _lifetime = Mathf.Max(0.2f, style.Lifetime);
            _pop = Mathf.Clamp(style.PopDuration, 0.04f, 0.2f);
            _arc = style.ArcStrength;
            _scalePeak = Mathf.Max(1f, style.ScalePeak);
            _baseCharSize = style.CharacterSize;
            _age = 0f;
            _active = true;
            _spawnPos = transform.position;
            float side = Random.value < 0.5f ? -1f : 1f;
            _arcDir = new Vector3(side * (0.55f + Random.value * 0.45f), 0f, 0f);

            _mesh.text = content ?? "";
            _mesh.color = _fill;
            _mesh.characterSize = _baseCharSize;
            _mesh.anchor = TextAnchor.MiddleCenter;
            _mesh.alignment = TextAlignment.Center;
            _mesh.fontSize = CombatTextCatalog.FontConfig.FontSize > 0
                ? CombatTextCatalog.FontConfig.FontSize
                : 48;
            // 动态字体图集可能延迟生成，播放时再同步一次
            if (_mesh.font != null && _mesh.font.material != null && _renderer != null
                && _renderer.sharedMaterial != null
                && _mesh.font.material.mainTexture != null)
            {
                _renderer.sharedMaterial.mainTexture = _mesh.font.material.mainTexture;
            }
            ApplyMaterialColors(1f);

            gameObject.SetActive(true);
        }

        /// <summary>池满复用时静默停用，不回调 Recycle。</summary>
        public void SilentDeactivate()
        {
            _active = false;
            _onDone = null;
            if (_mesh != null) _mesh.text = "";
            gameObject.SetActive(false);
        }
        #endregion

        #region 逐帧动画（缩放弹出 → 弧线上浮 → 朝向摄像机 → 淡出）
        void Update()
        {
            if (!_active) return;

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _lifetime);

            UpdateScale();
            UpdatePosition(t);
            UpdateFacing();
            UpdateColor(t);

            if (_age >= _lifetime)
                Finish();
        }

        /// <summary>弹出缩放（EaseOutBack）后回落定型。</summary>
        void UpdateScale()
        {
            float scale;
            if (_age < _pop)
            {
                float p = _age / _pop;
                scale = Mathf.Lerp(0.55f, _scalePeak, EaseOutBack(p));
            }
            else
            {
                float p = Mathf.Clamp01((_age - _pop) / Mathf.Max(0.01f, _lifetime - _pop));
                scale = Mathf.Lerp(_scalePeak, 0.85f, p);
            }

            _mesh.characterSize = _baseCharSize * scale;
        }

        /// <summary>弧线上浮（LoL 风格，非纯垂直）+ 暴击首 120ms 微抖动。</summary>
        void UpdatePosition(float t)
        {
            float rise = Mathf.Lerp(0f, 1.35f, EaseOutQuad(t));
            float side = Mathf.Sin(t * Mathf.PI) * _arc * 0.85f;
            transform.position = _spawnPos + Vector3.up * rise + _arcDir * side;

            if (_scalePeak > 1.55f && _age < 0.12f)
            {
                transform.position += new Vector3(
                    (Random.value - 0.5f) * 0.04f,
                    (Random.value - 0.5f) * 0.02f,
                    0f);
            }
        }

        /// <summary>始终朝向主摄像机。</summary>
        void UpdateFacing()
        {
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;
        }

        /// <summary>后半程线性淡出，同步写回 mesh 颜色与材质属性块。</summary>
        void UpdateColor(float t)
        {
            float alpha = t < 0.55f ? 1f : 1f - Mathf.InverseLerp(0.55f, 1f, t);
            var c = _fill;
            c.a = alpha;
            _mesh.color = c;
            ApplyMaterialColors(alpha);
        }
        #endregion

        #region 材质属性块 / 结束回收 / 缓动函数
        void ApplyMaterialColors(float alpha)
        {
            if (_renderer == null) return;
            var fill = _fill;
            fill.a = alpha;
            var outline = _outline;
            outline.a = _outline.a * alpha;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_Color", fill);
            _mpb.SetColor("_OutlineColor", outline);
            _renderer.SetPropertyBlock(_mpb);
        }

        void Finish()
        {
            _active = false;
            if (_mesh != null) _mesh.text = "";
            gameObject.SetActive(false);
            var cb = _onDone;
            _onDone = null;
            cb?.Invoke(this);
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
        #endregion
    }
}
