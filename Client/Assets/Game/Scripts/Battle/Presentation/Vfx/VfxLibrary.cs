using System;
using System.Collections;
using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>按 key 异步加载特效 Prefab；命中缓存时可同步实例化。</summary>
    public static class VfxLibrary
    {
        #region 常量与缓存
        public const string ResourcesFolder = "Vfx";

        static readonly Dictionary<string, GameObject> Cache =
            new Dictionary<string, GameObject>(32, StringComparer.OrdinalIgnoreCase);

        /// <summary>关战斗 / 清场时自增；异步回调对不上就丢弃，避免打完还往场景里塞特效。</summary>
        static int _spawnEpoch;
        #endregion

        #region 特效名整理 / Prefab 加载
        [Serializable]
        public class VfxAliasFile
        {
            public VfxAliasEntry[] Entries;
        }

        [Serializable]
        public class VfxAliasEntry
        {
            public string From;
            public string To;
        }

        static readonly Dictionary<string, string> Aliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static bool _aliasesLoaded;

        public static void ReloadAliases()
        {
            Aliases.Clear();
            Cache.Clear();
            _aliasesLoaded = true;
            if (!ConfigService.IsReady) return;

            var file = ConfigService.LoadFile<VfxAliasFile>("Presentation", "VfxAliases.json");
            if (file?.Entries == null || file.Entries.Length == 0)
            {
                Debug.LogWarning("[VfxLibrary] 未找到 Presentation/VfxAliases.json，仅用 Vfx_/Fx_ 前缀规则");
                return;
            }

            for (int i = 0; i < file.Entries.Length; i++)
            {
                var e = file.Entries[i];
                if (e == null || string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To))
                    continue;
                Aliases[e.From.Trim()] = e.To.Trim();
            }
        }

        static void EnsureAliases()
        {
            if (!_aliasesLoaded)
                ReloadAliases();
        }

        public static string NormalizeKey(string key)
        {
            EnsureAliases();
            if (string.IsNullOrEmpty(key)) return "";
            key = key.Trim();
            if (Aliases.TryGetValue(key, out var mapped))
                return mapped;

            if (key.StartsWith("Fx_", StringComparison.OrdinalIgnoreCase))
                key = "Vfx_" + key.Substring(3);
            else if (!key.StartsWith("Vfx_", StringComparison.OrdinalIgnoreCase))
                key = "Vfx_" + key;

            if (Aliases.TryGetValue(key, out mapped))
                return mapped;
            return key;
        }

        static string ToAssetKey(string norm) => $"{ResourcesFolder}/{norm}";

        /// <summary>已缓存则同步取；否则 null（请走 EnsurePrefabAsync）。</summary>
        public static GameObject TryGetCachedPrefab(string key)
        {
            string norm = NormalizeKey(key);
            if (string.IsNullOrEmpty(norm)) return null;
            return Cache.TryGetValue(norm, out var cached) && cached != null ? cached : null;
        }

        public static IEnumerator EnsurePrefabAsync(string key, Action<GameObject> onDone)
        {
            string norm = NormalizeKey(key);
            if (string.IsNullOrEmpty(norm))
            {
                onDone?.Invoke(null);
                yield break;
            }

            if (Cache.TryGetValue(norm, out var cached) && cached != null)
            {
                onDone?.Invoke(cached);
                yield break;
            }

            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(ToAssetKey(norm), p => prefab = p);
            if (prefab == null && !norm.StartsWith("Vfx_", StringComparison.OrdinalIgnoreCase))
                yield return BattleAssets.LoadAsync<GameObject>(
                    ToAssetKey("Vfx_" + norm), p => prefab = p);

            if (prefab != null)
                Cache[norm] = prefab;
            else
                Debug.LogWarning($"[VfxLibrary] Missing prefab {ResourcesFolder}/{norm}");

            onDone?.Invoke(prefab);
        }

        /// <summary>实例化到世界 Vfx 节点。已缓存则立刻返回；否则异步生成，onReady 回调。</summary>
        public static GameObject InstantiateWorld(
            string key, Vector3 worldPos, Quaternion rotation, Action<GameObject> onReady = null)
        {
            if (VfxManager.Instance == null)
            {
                onReady?.Invoke(null);
                return null;
            }

            int epoch = _spawnEpoch;
            var cached = TryGetCachedPrefab(key);
            if (cached != null)
            {
                var go = CreateWorldInstance(cached, key, worldPos, rotation);
                onReady?.Invoke(go);
                return go;
            }

            if (!BattleAssets.Run(CoInstantiateWorld(epoch, key, worldPos, rotation, onReady)))
                onReady?.Invoke(null);
            return null;
        }

        static IEnumerator CoInstantiateWorld(
            int epoch, string key, Vector3 worldPos, Quaternion rotation, Action<GameObject> onReady)
        {
            GameObject prefab = null;
            yield return EnsurePrefabAsync(key, p => prefab = p);
            if (!IsWorldSpawnAllowed(epoch) || prefab == null)
            {
                onReady?.Invoke(null);
                yield break;
            }

            var go = CreateWorldInstance(prefab, key, worldPos, rotation);
            onReady?.Invoke(go);
        }

        static GameObject CreateWorldInstance(
            GameObject prefab, string key, Vector3 worldPos, Quaternion rotation)
        {
            var parent = BattleScene.Vfx;
            var go = UnityEngine.Object.Instantiate(prefab, worldPos, rotation, parent);
            go.name = NormalizeKey(key);
            return go;
        }

        static GameObject CreateHost(string key, Vector3 worldPos, Quaternion rotation)
        {
            var go = new GameObject(NormalizeKey(key));
            var parent = BattleScene.Vfx;
            if (parent != null)
                go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, rotation);
            return go;
        }

        static bool IsWorldSpawnAllowed(int epoch) =>
            epoch == _spawnEpoch && VfxManager.Instance != null;
        #endregion

        #region 一次性特效生成
        /// <summary>
        /// 一次性特效。立刻返回宿主 TransientFx（视觉可稍后贴上），调用方始终能 Cancel。
        /// </summary>
        public static TransientFx SpawnTransient(
            string key,
            Vector3 worldPos,
            Vector3 forward,
            float durationOverride = -1f,
            Vector3? worldVelocity = null,
            bool persist = false,
            Color? tint = null,
            Vector3? scaleMul = null,
            Action<TransientFx> onSpawned = null)
        {
            if (VfxManager.Instance == null)
                return null;
            if (!TryGateByLod(worldPos, persist, out var lod))
                return null;

            forward = NormalizeForward(forward);
            var rot = Quaternion.LookRotation(forward, Vector3.up);
            var host = CreateHost(key, worldPos, rot);
            var fx = host.AddComponent<TransientFx>();
            ApplySpawnMotion(fx, persist, durationOverride, worldVelocity);

            var cached = TryGetCachedPrefab(key);
            if (cached != null)
            {
                AttachVisual(fx, cached, durationOverride, persist, tint, scaleMul, lod);
                onSpawned?.Invoke(fx);
                return fx;
            }

            int epoch = _spawnEpoch;
            if (!BattleAssets.Run(CoAttachTransient(
                epoch, fx, key, durationOverride, persist, tint, scaleMul, lod, onSpawned)))
            {
                fx.Cancel();
                return null;
            }
            return fx;
        }

        static IEnumerator CoAttachTransient(
            int epoch,
            TransientFx fx,
            string key,
            float durationOverride,
            bool persist,
            Color? tint,
            Vector3? scaleMul,
            VfxLodLevel lod,
            Action<TransientFx> onSpawned)
        {
            GameObject prefab = null;
            yield return EnsurePrefabAsync(key, p => prefab = p);

            if (fx == null || !IsWorldSpawnAllowed(epoch))
            {
                onSpawned?.Invoke(null);
                yield break;
            }

            if (prefab == null)
            {
                fx.Cancel();
                onSpawned?.Invoke(null);
                yield break;
            }

            AttachVisual(fx, prefab, durationOverride, persist, tint, scaleMul, lod);
            onSpawned?.Invoke(fx);
        }

        static void ApplySpawnMotion(
            TransientFx fx, bool persist, float durationOverride, Vector3? worldVelocity)
        {
            if (persist)
            {
                fx.PersistUntilCancel();
                return;
            }

            float dur = durationOverride > 0f ? durationOverride : 0.8f;
            fx.WithMotion(worldVelocity ?? Vector3.zero, dur);
        }

        static void AttachVisual(
            TransientFx fx,
            GameObject prefab,
            float durationOverride,
            bool persist,
            Color? tint,
            Vector3? scaleMul,
            VfxLodLevel lod)
        {
            if (fx == null || prefab == null) return;

            var visual = UnityEngine.Object.Instantiate(prefab, fx.transform);
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.name = "Visual";

            var nested = visual.GetComponent<TransientFx>();
            if (nested != null)
                UnityEngine.Object.Destroy(nested);

            if (!persist)
            {
                DisableLoopingParticles(visual);
                VfxLod.NotifySpawned();
                VfxLod.ApplyToInstance(fx.gameObject, lod);
                fx.MarkLodCounted();
            }

            ApplyScaleAndTint(fx.gameObject, scaleMul, tint);

            var root = visual.GetComponent<VfxPrefabRoot>();
            if (root == null)
                root = visual.GetComponentInChildren<VfxPrefabRoot>();

            float dur = durationOverride > 0f
                ? durationOverride
                : (root != null ? root.DefaultDuration : 0.45f);

            fx.BindPrefabVisual(
                root != null ? root.ResolveRenderer() : visual.GetComponentInChildren<Renderer>(),
                root == null || root.FadeOut,
                root != null && root.Shrink,
                root == null || root.Billboard,
                root != null && root.ExpandPulse);

            if (persist)
                fx.PersistUntilCancel();
            else
                fx.Duration = dur > TransientFx.MaxPersistSeconds
                    ? TransientFx.MaxPersistSeconds
                    : dur;
        }

        static void DisableLoopingParticles(GameObject go)
        {
            var particles = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;
                var main = particles[i].main;
                main.loop = false;
            }
        }

        private static bool TryGateByLod(Vector3 worldPos, bool persist, out VfxLodLevel lod)
        {
            lod = VfxLod.Evaluate(worldPos);
            if (persist) return true;
            if (lod >= VfxLodLevel.TextOnly) return false;
            if (!VfxLod.AllowVfxSpawn(worldPos)) return false;
            return true;
        }

        private static Vector3 NormalizeForward(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            return forward;
        }

        private static void ApplyScaleAndTint(GameObject go, Vector3? scaleMul, Color? tint)
        {
            if (scaleMul.HasValue)
                go.transform.localScale = Vector3.Scale(go.transform.localScale, scaleMul.Value);

            if (tint.HasValue)
                ApplyTint(go, tint.Value);
        }
        #endregion

        #region 着色 / 缓存清理
        public static void ApplyTint(GameObject root, Color color)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mat = r.material;
                if (mat == null) continue;
                if (mat.HasProperty("_Color")) mat.color = color;
                if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", color);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 0.55f);
                }
            }

            var particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                var ps = particles[i];
                var main = ps.main;
                main.startColor = color;
            }
        }

        public static void ClearCache() => Cache.Clear();

        /// <summary>清掉场景里还活着的特效，并作废进行中的异步生成。</summary>
        public static void ClearWorld()
        {
            _spawnEpoch++;
            var root = BattleScene.Vfx;
            if (root != null)
            {
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    var child = root.GetChild(i);
                    if (child != null)
                        UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            VfxLod.ResetCounters();
        }
        #endregion
    }
}
