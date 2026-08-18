using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>头顶名字、血条、持续状态字。数值读数据中心，跟着属性/改名刷新。</summary>
    public class HeadUIComponent : Component
    {
        #region UI 对象与状态
        private GameObject _root;
        private TextMesh _nameMesh;
        private Transform _hpBg;
        private Transform _hpFill;
        private Transform _statusRoot;
        private readonly List<TextMesh> _statusMeshes = new List<TextMesh>(4);
        private string _displayName;
        private Color _nameColor = Color.white;
        private bool _showName = true;
        private bool _visible = true;
        #endregion

        #region 依赖组件
        private ViewComponent _viewComp;
        private TransformComponent _transformComp;

        private ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        #endregion

        #region 血条尺寸
        private const float BarWidth = 1.1f;
        private const float BarHeight = 0.12f;
        private const float NameAboveBar = 0.22f;
        private const float StatusLineGap = 0.14f;
        private const float StatusRightGap = 0.08f;
        private float _barWidth = BarWidth;
        private float _barHeight = BarHeight;
        #endregion

        #region 初始化
        public void InitHeadText(string nameText, Color textColor, bool showName = true)
        {
            _displayName = string.IsNullOrEmpty(nameText) ? "Unknown" : nameText;
            _nameColor = textColor;
            _showName = showName;
            ResolveBarSize();

            _root = new GameObject("HeadUI_Root");
            _root.transform.SetParent(BattleScene.Entities, true);

            if (_showName)
                CreateNameLabel();

            CreateHealthBars(textColor);

            RefreshHpBar();
            Owner?.AddListener<AttrChange>("OnAttrChanged", OnAttrChanged);
            Owner?.AddListener<string>("OnDisplayNameChanged", OnDisplayNameChanged);
        }

        /// <summary>创建名字 TextMesh（挂在 _root 下）。</summary>
        private void CreateNameLabel()
        {
            var nameGo = new GameObject("NameLabel");
            nameGo.transform.SetParent(_root.transform, false);
            nameGo.transform.localPosition = new Vector3(0f, NameAboveBar * (_barWidth / BarWidth), 0f);

            _nameMesh = nameGo.AddComponent<TextMesh>();
            _nameMesh.text = _displayName;
            _nameMesh.color = _nameColor;
            _nameMesh.fontSize = 48;
            _nameMesh.characterSize = IsMinionOwner() ? 0.045f : 0.06f;
            _nameMesh.anchor = TextAnchor.LowerCenter;
            _nameMesh.alignment = TextAlignment.Center;
        }

        /// <summary>创建血条背景与前景（挂在 _root 下）。</summary>
        private void CreateHealthBars(Color textColor)
        {
            _hpBg = CreateBarQuad("HpBg", new Color(0.08f, 0.08f, 0.1f, 0.9f), _barWidth, _barHeight);
            _hpBg.SetParent(_root.transform, false);
            _hpBg.localPosition = Vector3.zero;

            // 血条跟阵营色走（名字色即队伍色），更接近 LoL 敌我辨识
            Color fill = Color.Lerp(textColor, new Color(0.95f, 0.95f, 0.95f), 0.12f);
            fill.a = 0.95f;
            _hpFill = CreateBarQuad("HpFill", fill, _barWidth, _barHeight * 0.85f);
            _hpFill.SetParent(_root.transform, false);
            _hpFill.localPosition = new Vector3(0f, 0f, -0.01f);
        }
        #endregion

        #region 公开 API
        public void SetDisplayName(string nameText)
        {
            if (!_showName || string.IsNullOrEmpty(nameText)) return;
            _displayName = nameText;
            if (_nameMesh != null)
                _nameMesh.text = _displayName;
        }

        /// <summary>死亡瞬间隐藏血条/名字。</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_root != null)
                _root.SetActive(visible);
        }

        /// <summary>同步头顶持续状态字；空列表则清空。排在名字/血条右侧，多条自上而下。</summary>
        public void SetStatusLabels(IReadOnlyList<(string Text, Color Color)> labels)
        {
            if (_root == null) return;
            EnsureStatusRoot();

            int count = labels != null ? labels.Count : 0;
            while (_statusMeshes.Count < count)
            {
                var go = new GameObject($"Status_{_statusMeshes.Count}");
                go.transform.SetParent(_statusRoot, false);
                var mesh = go.AddComponent<TextMesh>();
                mesh.fontSize = 42;
                mesh.characterSize = IsMinionOwner() ? 0.04f : 0.052f;
                mesh.anchor = TextAnchor.MiddleLeft;
                mesh.alignment = TextAlignment.Left;
                _statusMeshes.Add(mesh);
            }

            float x = _barWidth * 0.5f + StatusRightGap;
            // 与名字、血条中段对齐：血条在 y=0，名字在上方，状态块从中间起向下排
            float baseY = NameAboveBar * (_barWidth / BarWidth) * 0.5f;
            for (int i = 0; i < _statusMeshes.Count; i++)
            {
                var mesh = _statusMeshes[i];
                if (mesh == null) continue;
                bool on = i < count && !string.IsNullOrEmpty(labels[i].Text);
                mesh.gameObject.SetActive(on);
                if (!on) continue;
                mesh.text = labels[i].Text;
                mesh.color = labels[i].Color;
                float y = baseY - StatusLineGap * i;
                mesh.transform.localPosition = new Vector3(x, y, 0f);
            }
        }

        void EnsureStatusRoot()
        {
            if (_statusRoot != null) return;
            var go = new GameObject("StatusLabels");
            go.transform.SetParent(_root.transform, false);
            go.transform.localPosition = Vector3.zero;
            _statusRoot = go.transform;
        }

        private void OnDisplayNameChanged(string name)
        {
            SetDisplayName(name);
        }
        #endregion

        #region 内部：血条外观 / 尺寸 / 刷新
        static Mesh _barQuadMesh;

        /// <summary>头顶血条：代码生成 XY 平面片（朝 -Z），跟相机旋转后正对屏幕；不复用单位占位方块。</summary>
        private static Transform CreateBarQuad(string name, Color color, float width, float height)
        {
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = GetBarQuadMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            BattleUnlitTint.Apply(renderer, color);
            go.transform.localScale = new Vector3(width, height, 1f);
            return go.transform;
        }

        static Mesh GetBarQuadMesh()
        {
            if (_barQuadMesh != null)
                return _barQuadMesh;

            // 与 Unity Quad 一致：落在 XY，朝向 -Z，父节点抄相机旋转后正对画面
            _barQuadMesh = new Mesh { name = "HeadUI_BarQuad" };
            _barQuadMesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            _barQuadMesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            _barQuadMesh.RecalculateBounds();
            return _barQuadMesh;
        }

        void ResolveBarSize()
        {
            if (IsMinionOwner())
            {
                _barWidth = 0.62f;
                _barHeight = 0.07f;
            }
            else
            {
                _barWidth = BarWidth;
                _barHeight = BarHeight;
            }
        }

        bool IsMinionOwner()
        {
            if (Owner == null || BattleCache.Instance == null) return false;
            if (!BattleCache.Instance.TryGet(Owner.Id, out var data) || data == null) return false;
            int t = data.EntityType;
            return t == EEntityType.MinionMelee
                   || t == EEntityType.MinionRanged
                   || t == EEntityType.MinionSiege
                   || t == EEntityType.MinionSuper;
        }

        private void OnAttrChanged(AttrChange change)
        {
            if (change.Id == EAttrId.Hp || change.Id == EAttrId.MaxHp)
                RefreshHpBar();
        }

        private void RefreshHpBar()
        {
            if (_hpFill == null) return;

            float ratio = 1f;
            var cache = BattleCache.Instance;
            if (cache != null && Owner != null && cache.TryGet(Owner.Id, out var data) && data.MaxHp > 0.01f)
                ratio = Mathf.Clamp01(data.Hp / data.MaxHp);

            _hpFill.localScale = new Vector3(_barWidth * ratio, _barHeight * 0.85f, 1f);
            _hpFill.localPosition = new Vector3(-_barWidth * 0.5f + (_barWidth * ratio) * 0.5f, 0f, -0.01f);

            var fillRenderer = _hpFill.GetComponent<Renderer>();
            if (fillRenderer != null)
            {
                Color c = Color.Lerp(new Color(0.9f, 0.2f, 0.15f), new Color(0.25f, 0.85f, 0.35f), ratio);
                BattleUnlitTint.SetColor(fillRenderer, c);
            }
        }
        #endregion

        #region 生命周期
        public override void OnLateUpdate(float dt)
        {
            if (_root == null || !_visible) return;

            var viewComp = ViewComp;
            Vector3 anchor;
            if (viewComp != null)
                anchor = viewComp.GetHeadAnchorWorldPos(0.15f);
            else
            {
                var transformComp = TransformComp;
                anchor = transformComp != null ? transformComp.Position + Vector3.up * 2.1f : Vector3.zero;
            }

            _root.transform.position = anchor;

            if (Camera.main != null)
                _root.transform.rotation = Camera.main.transform.rotation;
        }

        public override void OnDestroy()
        {
            Owner?.RemoveListener<AttrChange>("OnAttrChanged", OnAttrChanged);
            Owner?.RemoveListener<string>("OnDisplayNameChanged", OnDisplayNameChanged);
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }
        }
        #endregion
    }
}
