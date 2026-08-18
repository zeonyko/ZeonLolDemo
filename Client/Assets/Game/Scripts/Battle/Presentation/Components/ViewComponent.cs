using System.Collections.Generic;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>角色外观。逻辑位置在脚底；画面慢慢跟上，不另做一套移动。颜色（本体/控制染色/受击闪白）只在这里合成。</summary>
    public class ViewComponent : Component
    {
        public GameObject ViewGameObject { get; private set; }

        /// <summary>模型总高度（已含缩放）</summary>
        public float VisualHeight { get; private set; } = 2f;

        /// <summary>脚底到模型中心的偏移</summary>
        public float PivotOffsetY { get; private set; } = 1f;

        /// <summary>画面跟上逻辑的速度。越大越跟手；闪现/跟服务器对齐时会滑过去。</summary>
        public float LogicFollowSpeed = 22f;

        /// <summary>当前画面脚底（冲刺插值中是表现位置，不是逻辑坐标）。</summary>
        public Vector3 DisplayFeet =>
            _hasDisplay ? _displayFeet : (TransformComp != null ? TransformComp.Position : Vector3.zero);

        #region 依赖组件
        private TransformComponent _transformComp;
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        #endregion

        #region 画面慢慢跟上逻辑位置
        private Vector3 _displayFeet; // 当前画面脚底，慢慢跟上逻辑位置
        private bool _hasDisplay;     // 画面脚底是否已初始化

        /// <summary>LinearDash 表现：按时长从当前画面插到落点，不走通用 lerp。</summary>
        private sealed class DashVisualState
        {
            public bool Active;
            public Vector3 From;
            public Vector3 To;
            public float Duration;
            public float Elapsed;

            public void Clear()
            {
                Active = false;
                Duration = 0f;
                Elapsed = 0f;
            }
        }

        private readonly DashVisualState _dash = new DashVisualState();
        private const float MinDashVisualDuration = 0.04f;
        #endregion

        #region 身体颜色（本体 / 控制染色 / 受击闪白）
        /// <summary>三层颜色在此合成后写入模型。</summary>
        private sealed class BodyColorState
        {
            public Color BaseColor = Color.white;   // 阵营色/初始色
            public bool HasBaseColor;                // 是否设过本体色
            public Renderer BodyRenderer;            // 主渲染器（没有阵营色块时用）
            public readonly List<Renderer> TintRenderers = new List<Renderer>(6); // 要染色的部位（通常是 TeamTint*）

            public bool HasControlTint;  // 是否被眩晕/冰冻等强制上色
            public Color ControlTint;    // 控制效果颜色
            public float FlashBlend;     // 受击/死亡闪白强度（0~1）
            public Color FlashColor = Color.white; // 闪白颜色
        }

        private readonly BodyColorState _bodyColor = new BodyColorState();
        #endregion

        #region 生命周期与事件订阅
        public override void OnStart()
        {
            Owner?.AddListener<Vector3>("OnPositionSnap", OnLogicPositionSnap);
            Owner?.AddListener<string>("OnDisplayNameChanged", OnDisplayNameChanged);
        }

        public override void OnDestroy()
        {
            Owner?.RemoveListener<Vector3>("OnPositionSnap", OnLogicPositionSnap);
            Owner?.RemoveListener<string>("OnDisplayNameChanged", OnDisplayNameChanged);
            if (ViewGameObject != null)
            {
                Object.Destroy(ViewGameObject);
                ViewGameObject = null;
            }
        }

        private void OnDisplayNameChanged(string name)
        {
            if (ViewGameObject == null || string.IsNullOrEmpty(name) || Owner == null) return;
            ViewGameObject.name = $"{name}_{Owner.Id}";
        }

        private void OnLogicPositionSnap(Vector3 _)
        {
            // 立刻贴齐，避免慢慢跟上看起来像滑过去
            SnapToLogic();
        }
        #endregion

        #region 身体颜色（本体 / 控制染色 / 受击闪白）
        public void RememberBaseBodyColor(Color color)
        {
            _bodyColor.BaseColor = color;
            _bodyColor.HasBaseColor = true;
            ApplyBodyColor();
        }

        public void SetBodyTint(Color tint)
        {
            _bodyColor.HasControlTint = true;
            _bodyColor.ControlTint = tint;
            ApplyBodyColor();
        }

        public void ClearBodyTint()
        {
            _bodyColor.HasControlTint = false;
            ApplyBodyColor();
        }

        /// <summary>受击/死亡闪白；blend=0 清除闪白层。</summary>
        public void SetHitFlash(Color flashColor, float blend01)
        {
            _bodyColor.FlashColor = flashColor;
            _bodyColor.FlashBlend = Mathf.Clamp01(blend01);
            ApplyBodyColor();
        }

        public void ClearHitFlash()
        {
            if (_bodyColor.FlashBlend <= 0f) return;
            _bodyColor.FlashBlend = 0f;
            ApplyBodyColor();
        }

        /// <summary>合成本体色、控制染色、受击闪白，写入模型。</summary>
        private void ApplyBodyColor()
        {
            EnsureBodyRenderer();
            var tintRenderers = _bodyColor.TintRenderers;
            if (tintRenderers.Count == 0 && _bodyColor.BodyRenderer == null) return;

            Color under = _bodyColor.HasControlTint ? _bodyColor.ControlTint : _bodyColor.BaseColor;
            if (!_bodyColor.HasBaseColor && !_bodyColor.HasControlTint && _bodyColor.BodyRenderer != null
                && _bodyColor.BodyRenderer.sharedMaterial != null
                && _bodyColor.BodyRenderer.sharedMaterial.HasProperty("_Color"))
                under = _bodyColor.BodyRenderer.sharedMaterial.GetColor("_Color");

            Color final = _bodyColor.FlashBlend > 0.001f
                ? Color.Lerp(under, _bodyColor.FlashColor, _bodyColor.FlashBlend)
                : under;

            if (tintRenderers.Count > 0)
            {
                for (int i = 0; i < tintRenderers.Count; i++)
                {
                    if (tintRenderers[i] != null)
                        BattleUnlitTint.SetColor(tintRenderers[i], final);
                }
                return;
            }

            if (_bodyColor.BodyRenderer != null)
                BattleUnlitTint.SetColor(_bodyColor.BodyRenderer, final);
        }

        private void EnsureBodyRenderer()
        {
            if (ViewGameObject == null) return;
            if (_bodyColor.TintRenderers.Count == 0 && _bodyColor.BodyRenderer == null)
            {
                CollectTintRenderers(ViewGameObject, _bodyColor.TintRenderers, out var primary);
                _bodyColor.BodyRenderer = primary;
            }
            else if (_bodyColor.BodyRenderer == null)
                _bodyColor.BodyRenderer = ViewGameObject.GetComponentInChildren<Renderer>();
        }
        #endregion

        #region 视图创建：Prefab / 占位
        /// <summary>按资源 key 生成角色 Prefab（同步，仅当已在缓存中）。</summary>
        public void InitViewFromPrefab(string resourcesPath, Color? tint = null)
        {
            var prefab = BattleAssets.Load<GameObject>(resourcesPath);
            if (prefab == null)
            {
                Debug.LogError($"[ViewComponent] Prefab not found: {resourcesPath}");
                return;
            }

            ApplyFormalPrefab(prefab, tint);
        }

        /// <summary>异步换成正式外观；失败则保留当前占位。</summary>
        public void RequestFormalView(string resourcesPath, Color? tint = null)
        {
            if (string.IsNullOrEmpty(resourcesPath))
                return;
            BattleAssets.Run(CoRequestFormalView(resourcesPath, tint));
        }

        System.Collections.IEnumerator CoRequestFormalView(string resourcesPath, Color? tint)
        {
            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(resourcesPath, p => prefab = p);
            if (prefab == null || Owner == null)
            {
                if (prefab == null)
                    Debug.LogWarning("[ViewComponent] 正式 Prefab 加载失败，保留占位 key=" + resourcesPath);
                yield break;
            }

            ApplyFormalPrefab(prefab, tint);
        }

        void ApplyFormalPrefab(GameObject prefab, Color? tint)
        {
            InstantiatePrefabView(prefab);
            StripColliders(ViewGameObject);
            ApplyPrefabBodyColor(tint);
            MeasureAndSyncExtents();
            Owner?.GetComponent<AnimationComponent>()?.BindAnimator();
        }

        /// <summary>拆掉旧外观，生成 Prefab 并挂到角色节点下。</summary>
        private void InstantiatePrefabView(GameObject prefab)
        {
            if (ViewGameObject != null)
            {
                Object.Destroy(ViewGameObject);
                ViewGameObject = null;
            }

            ViewGameObject = Object.Instantiate(prefab);
            string label = string.IsNullOrEmpty(Owner.Name) ? $"Entity_{Owner.Id}" : Owner.Name;
            ViewGameObject.name = $"{label}_{Owner.Id}";
            ViewGameObject.transform.SetParent(BattleScene.Entities, true);
        }

        /// <summary>位置由逻辑管，去掉物理碰撞避免互推。</summary>
        private static void StripColliders(GameObject root)
        {
            if (root == null) return;
            var cols = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                Object.Destroy(cols[i]);
        }

        /// <summary>找出要染色的部位，按需写入本体色并合成。</summary>
        private void ApplyPrefabBodyColor(Color? tint)
        {
            CollectTintRenderers(ViewGameObject, _bodyColor.TintRenderers, out var primaryRenderer);
            _bodyColor.BodyRenderer = primaryRenderer;
            _bodyColor.HasControlTint = false;
            _bodyColor.FlashBlend = 0f;

            if (tint.HasValue && (_bodyColor.TintRenderers.Count > 0 || _bodyColor.BodyRenderer != null))
            {
                _bodyColor.BaseColor = tint.Value;
                _bodyColor.HasBaseColor = true;
                ApplyBodyColor();
            }
            else
            {
                _bodyColor.HasBaseColor = false;
            }
        }

        /// <summary>量 Prefab 身高，记下高度和脚底偏移，画面立刻贴齐。</summary>
        private void MeasureAndSyncExtents()
        {
            MeasurePrefabExtents(ViewGameObject, out float height, out float pivotY);
            VisualHeight = height;
            PivotOffsetY = pivotY;
            SyncTransform(snap: true, 0f);
        }

        /// <summary>优先染 TeamTint* 阵营色块，避免改坏模型贴图。</summary>
        private static void CollectTintRenderers(GameObject root, List<Renderer> into, out Renderer primary)
        {
            into.Clear();
            primary = null;
            if (root == null) return;

            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                string n = t.name;
                if (n != "TeamTint" && !n.StartsWith("TeamTint_")) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                into.Add(r);
                if (primary == null) primary = r;
            }

            if (into.Count > 0) return;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "Body")
                {
                    primary = all[i].GetComponent<Renderer>();
                    if (primary != null) return;
                }
            }

            primary = root.GetComponentInChildren<Renderer>();
        }

        /// <summary>Prefab 约定根节点在脚底附近。偏移为 0 时，画面位置就是逻辑脚底。</summary>
        private static void MeasurePrefabExtents(GameObject root, out float height, out float pivotY)
        {
            height = 2f;
            pivotY = 0f;
            if (root == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0) return;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);

            height = Mathf.Max(0.5f, b.size.y);
            // 根在脚底：世界高度接近包围盒底；若枢轴在模型中心则抬半个身高
            float rootY = root.transform.position.y;
            float bottomGap = b.min.y - rootY;
            if (bottomGap > height * 0.25f)
                pivotY = height * 0.5f;
            else
                pivotY = 0f;
        }

        /// <summary>占位外观：用时 LoadAsync；已缓存则立刻挂上。</summary>
        public void InitPlaceholderView(Color color, Vector3 scale)
        {
            var prefab = BattleAssets.Load<GameObject>(BattlePlaceholderKeys.Default);
            if (prefab != null)
            {
                ApplyPlaceholder(prefab, color, scale);
                return;
            }

            BattleAssets.Run(CoInitPlaceholder(color, scale));
        }

        System.Collections.IEnumerator CoInitPlaceholder(Color color, Vector3 scale)
        {
            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(BattlePlaceholderKeys.Default, p => prefab = p);
            if (prefab == null || Owner == null)
            {
                Debug.LogError("[ViewComponent] InitPlaceholderView 失败 key=" + BattlePlaceholderKeys.Default);
                yield break;
            }

            ApplyPlaceholder(prefab, color, scale);
        }

        void ApplyPlaceholder(GameObject prefab, Color color, Vector3 scale)
        {
            InstantiatePrefabView(prefab);
            ViewGameObject.transform.localScale = scale;

            Color body = SoftPlaceholderColor(color);

            _bodyColor.TintRenderers.Clear();
            _bodyColor.BodyRenderer = ViewGameObject.GetComponentInChildren<Renderer>();
            if (_bodyColor.BodyRenderer != null)
            {
                BattleUnlitTint.Apply(_bodyColor.BodyRenderer, body);
                _bodyColor.TintRenderers.Add(_bodyColor.BodyRenderer);
            }

            _bodyColor.BaseColor = body;
            _bodyColor.HasBaseColor = true;
            _bodyColor.HasControlTint = false;
            _bodyColor.FlashBlend = 0f;
            ApplyBodyColor();

            VisualHeight = scale.y;
            PivotOffsetY = VisualHeight * 0.5f;

            AttachFacingMarker(body);

            SyncTransform(snap: true, 0f);
        }

        static Color SoftPlaceholderColor(Color team)
        {
            var muted = Color.Lerp(team, new Color(0.42f, 0.44f, 0.48f, 1f), 0.35f);
            muted.a = 0.72f;
            return muted;
        }

        /// <summary>正前方小尖：复用兜底 Prefab。</summary>
        private void AttachFacingMarker(Color bodyColor)
        {
            if (ViewGameObject == null) return;
            var tipPrefab = BattleAssets.Load<GameObject>(BattlePlaceholderKeys.Default);
            if (tipPrefab == null)
                return;

            var nose = Object.Instantiate(tipPrefab, ViewGameObject.transform, false);
            nose.name = "FacingMarker";

            Color tip = Color.Lerp(bodyColor, new Color(0.15f, 0.16f, 0.18f, 1f), 0.4f);
            tip.a = Mathf.Min(bodyColor.a + 0.1f, 0.9f);
            var noseRenderer = nose.GetComponentInChildren<Renderer>();
            if (noseRenderer != null)
                BattleUnlitTint.Apply(noseRenderer, tip);

            nose.transform.localPosition = new Vector3(0f, 0.15f, 0.55f);
            nose.transform.localScale = new Vector3(0.28f, 0.16f, 0.38f);
            nose.transform.localRotation = Quaternion.identity;
        }
        #endregion

        #region 每帧同步渲染位置
        public override void OnLateUpdate(float dt)
        {
            SyncTransform(snap: false, dt);
        }

        /// <summary>画面立刻贴齐逻辑位置（生成 / 闪现 / 打断）。</summary>
        public void SnapToLogic()
        {
            _dash.Clear();
            SyncTransform(snap: true, 0f);
        }

        /// <summary>冲刺表现：从当前画面插到落点。时长过短则直接贴齐。</summary>
        public void BeginDashVisual(Vector3 landPos, float duration)
        {
            if (ViewGameObject == null) return;
            if (duration < MinDashVisualDuration)
            {
                SnapToLogic();
                return;
            }

            if (!_hasDisplay && TransformComp != null)
            {
                _displayFeet = TransformComp.Position;
                _hasDisplay = true;
            }

            _dash.Active = true;
            _dash.From = _displayFeet;
            _dash.To = landPos;
            _dash.Duration = duration;
            _dash.Elapsed = 0f;
        }

        /// <summary>冲刺 clip 结束：若还在插值则贴齐落点。</summary>
        public void EndDashVisual()
        {
            if (!_dash.Active) return;
            _dash.Clear();
            SyncTransform(snap: true, 0f);
        }

        Vector3 _faceDir = Vector3.forward;
        bool _hasFace;
        bool _snapFace;
        float _faceTurnSpeed = 18f;

        public Vector3 FaceDir
        {
            get
            {
                if (_hasFace) return _faceDir;
                if (ViewGameObject == null) return Vector3.forward;
                Vector3 f = ViewGameObject.transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        public void Face(Vector3 dir, float turnSpeed)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            _faceDir = dir.normalized;
            _hasFace = true;
            _faceTurnSpeed = turnSpeed > 0.1f ? turnSpeed : 18f;
        }

        public void SnapFace(Vector3 dir)
        {
            Face(dir, 18f);
            _snapFace = true;
        }

        void ApplyFace(bool snap, float dt)
        {
            if (!_hasFace || ViewGameObject == null) return;
            Quaternion target = Quaternion.LookRotation(_faceDir);
            if (snap || _snapFace)
            {
                ViewGameObject.transform.rotation = target;
                _snapFace = false;
                return;
            }

            float step = dt > 0f ? dt : Time.deltaTime;
            ViewGameObject.transform.rotation = Quaternion.Slerp(
                ViewGameObject.transform.rotation, target, step * _faceTurnSpeed);
        }

        private void SyncTransform(bool snap, float dt)
        {
            var transformComp = TransformComp;
            if (transformComp == null || ViewGameObject == null) return;

            Vector3 feet = transformComp.Position;
            if (snap || !_hasDisplay)
            {
                _dash.Clear();
                _displayFeet = feet;
                _hasDisplay = true;
            }
            else if (_dash.Active)
            {
                float step = dt > 0f ? dt : Time.deltaTime;
                _dash.Elapsed += step;
                float u = _dash.Duration > 0.0001f ? _dash.Elapsed / _dash.Duration : 1f;
                u = Mathf.Clamp01(u);
                u = u * u * (3f - 2f * u);
                _displayFeet = Vector3.Lerp(_dash.From, _dash.To, u);
                if (u >= 1f)
                    _dash.Clear();
            }
            else
            {
                float t = 1f - Mathf.Exp(-LogicFollowSpeed * Time.deltaTime);
                _displayFeet = Vector3.Lerp(_displayFeet, feet, t);
            }

            ViewGameObject.transform.position = _displayFeet + new Vector3(0f, PivotOffsetY, 0f);
            ApplyFace(snap, dt);
        }

        /// <summary>头顶世界坐标，给名字和血条用。</summary>
        public Vector3 GetHeadAnchorWorldPos(float extraPadding = 0.2f)
        {
            var transformComp = TransformComp;
            if (transformComp == null)
                return Vector3.zero;

            return transformComp.Position + new Vector3(0f, VisualHeight + extraPadding, 0f);
        }
        #endregion
    }
}
