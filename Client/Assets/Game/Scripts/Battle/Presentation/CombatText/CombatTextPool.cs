using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>飘字复用：预先准备、用完回收、太多就挤掉最旧的。</summary>
    public sealed class CombatTextPool
    {
        #region 字段
        public static CombatTextPool Instance { get; private set; }

        readonly Stack<CombatTextItem> _free = new Stack<CombatTextItem>(64);
        readonly List<CombatTextItem> _live = new List<CombatTextItem>(64);
        readonly Dictionary<Vector3Int, int> _stackBuckets = new Dictionary<Vector3Int, int>(32);
        Transform _root;
        Font _font;
        Material _sharedMat;
        int _max;
        bool _bootstrapped;
        bool _ready;
        #endregion

        #region 生命周期
        public static CombatTextPool Create()
        {
            Instance = new CombatTextPool();
            Instance.BootstrapStructure();
            return Instance;
        }

        public static IEnumerator EnsureReadyAsync()
        {
            var pool = Instance ?? Create();
            yield return pool.CoEnsureResources();
        }

        public void Shutdown()
        {
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i] != null)
                    Object.Destroy(_live[i].gameObject);
            }
            while (_free.Count > 0)
            {
                var item = _free.Pop();
                if (item != null)
                    Object.Destroy(item.gameObject);
            }
            _live.Clear();
            _free.Clear();
            if (_root != null)
                Object.Destroy(_root.gameObject);
            _root = null;
            _ready = false;
            if (Instance == this)
                Instance = null;
        }

        void BootstrapStructure()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;
            CombatTextCatalog.Reload();
            var cfg = CombatTextCatalog.Styles;
            _max = Mathf.Clamp(cfg.PoolMax, 16, 128);
            _root = new GameObject("CombatTextPool").transform;
            _root.SetParent(BattleScene.Vfx, false);
        }

        IEnumerator CoEnsureResources()
        {
            if (_ready) yield break;
            BootstrapStructure();

            var fc = CombatTextCatalog.FontConfig;
            if (!string.IsNullOrEmpty(fc.PreferredFontResource))
                yield return BattleAssets.LoadAsync<Font>(fc.PreferredFontResource, f => _font = f);
            if (_font == null)
                _font = ResolveFallbackFont(fc);
            if (!string.IsNullOrEmpty(fc.MaterialResource))
                yield return BattleAssets.LoadAsync<Material>(fc.MaterialResource, m => _sharedMat = m);

            if (_font == null)
                Debug.LogError("[CombatText] 缺少字体 " + (fc.PreferredFontResource ?? ""));
            if (_sharedMat == null)
                Debug.LogError("[CombatText] 缺少描边材质 " + fc.MaterialResource);
            else if (_font != null && _font.material != null && _font.material.mainTexture != null
                     && _sharedMat.mainTexture == null)
                _sharedMat.mainTexture = _font.material.mainTexture;

            int prewarm = Mathf.Clamp(CombatTextCatalog.Styles.PoolPrewarm, 0, _max);
            for (int i = _free.Count; i < prewarm; i++)
                _free.Push(CreateItem());

            _ready = true;
        }
        #endregion

        #region 生成与回收
        /// <summary>取一个飘字条目播放；池未满则新建，池已满则挤掉最旧的一条。</summary>
        public CombatTextItem Spawn(
            Vector3 worldPos,
            string content,
            CombatTextKind kind,
            Color? overrideColor = null)
        {
            if (!_ready)
            {
                BattleAssets.Run(CoEnsureResources());
                return null;
            }

            worldPos = ApplyStackOffset(worldPos);
            CombatTextItem item;
            if (_free.Count > 0)
            {
                item = _free.Pop();
            }
            else if (_live.Count < _max)
            {
                item = CreateItem();
            }
            else
            {
                item = _live[0];
                _live.RemoveAt(0);
                item.SilentDeactivate();
            }

            if (item == null) return null;

            item.transform.position = worldPos + Random.insideUnitSphere * 0.08f;
            var style = CombatTextCatalog.GetStyle(kind);
            item.Play(content, style, overrideColor, Recycle);
            _live.Add(item);
            return item;
        }

        /// <summary>条目播放结束的回调：放回空闲栈，池已满则直接销毁。</summary>
        void Recycle(CombatTextItem item)
        {
            if (item == null) return;
            _live.Remove(item);
            if (_free.Count < _max)
                _free.Push(item);
            else
                Object.Destroy(item.gameObject);
        }
        #endregion

        #region 堆叠偏移
        /// <summary>粗网格堆叠偏移，避免同点数字完全重叠。</summary>
        Vector3 ApplyStackOffset(Vector3 worldPos)
        {
            // 粗网格堆叠，避免同点数字完全重叠
            var key = new Vector3Int(
                Mathf.RoundToInt(worldPos.x * 2f),
                Mathf.RoundToInt(worldPos.y * 2f),
                Mathf.RoundToInt(worldPos.z * 2f));
            _stackBuckets.TryGetValue(key, out int n);
            _stackBuckets[key] = n + 1;
            float dy = CombatTextCatalog.Styles.StackOffsetY * (n % 5);
            // 周期性清理，防止字典膨胀
            if (_stackBuckets.Count > 128)
                _stackBuckets.Clear();
            return worldPos + Vector3.up * dy;
        }
        #endregion

        #region 条目创建与字体/材质解析
        static Font ResolveFallbackFont(CombatTextFontConfigFile fc)
        {
            string[] names = fc != null ? fc.OsFontFallbacks : null;
            int size = fc != null && fc.FontSize > 0 ? fc.FontSize : 48;
            if (names != null)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.IsNullOrEmpty(names[i])) continue;
                    var os = Font.CreateDynamicFontFromOSFont(names[i], size);
                    if (os != null)
                        return os;
                }
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        /// <summary>创建一个隐藏状态的飘字 GameObject（TextMesh + CombatTextItem）。</summary>
        CombatTextItem CreateItem()
        {
            var go = new GameObject("CombatText");
            go.transform.SetParent(_root, false);
            go.SetActive(false);

            var mesh = go.AddComponent<TextMesh>();
            mesh.font = _font;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = 0.062f;
            mesh.fontSize = CombatTextCatalog.FontConfig.FontSize > 0
                ? CombatTextCatalog.FontConfig.FontSize
                : 72;

            var renderer = go.GetComponent<MeshRenderer>();
            if (_sharedMat != null)
                renderer.sharedMaterial = _sharedMat;
            else if (_font != null && _font.material != null)
                renderer.sharedMaterial = _font.material;

            var item = go.AddComponent<CombatTextItem>();
            item.Bind(mesh, renderer);
            return item;
        }
        #endregion
    }
}
