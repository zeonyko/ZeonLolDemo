using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Game.ZeonAsset;
using Object = UnityEngine.Object;

namespace Client.Battle
{
    /// <summary>战斗资源加载器：编辑器可同步直读；真机主路径异步。</summary>
    public interface IBattleAssetLoader
    {
        /// <summary>同步：编辑器磁盘直读；真机仅返回已在缓存中的资源。</summary>
        T LoadSync<T>(string location) where T : Object;

        /// <summary>location 为 <c>Assets/Game/...</c> 完整路径（含扩展名）。</summary>
        IEnumerator LoadAsync(string location, Type assetType, Action<Object> onDone);
    }

    /// <summary>
    /// 战斗资源入口。Tag 负责下载到磁盘；内存主路径 <see cref="LoadAsync{T}"/>。
    /// Key 是 <c>Assets/Game</c> 下的相对路径；无扩展名时按 <typeparamref name="T"/> 补一个。
    /// <see cref="PreloadAsync"/> 可选暖场，不填则用时再加载。
    /// </summary>
    public static class BattleAssets
    {
        public static IBattleAssetLoader Loader { get; private set; }

        static readonly Dictionary<string, Object> Cache =
            new Dictionary<string, Object>(StringComparer.OrdinalIgnoreCase);

        static MonoBehaviour _runner;

        public const string BattleResourceTag = "Game";

        public static void Initialize(IBattleAssetLoader loader)
        {
            Loader = loader;
            Cache.Clear();
        }

        public static void BindRunner(MonoBehaviour runner) => _runner = runner;

        public static void Shutdown()
        {
            Loader = null;
            _runner = null;
            Cache.Clear();
        }

        public static bool Run(IEnumerator routine)
        {
            if (routine == null)
                return false;
            if (_runner == null)
            {
                Debug.LogError("[BattleAssets] 未 BindRunner，无法启动异步加载");
                return false;
            }

            _runner.StartCoroutine(routine);
            return true;
        }

        public static void Put(string key, Object asset)
        {
            if (string.IsNullOrEmpty(key) || asset == null)
                return;
            Cache[NormalizeKey(key)] = asset;
        }

        public static bool TryGetCached<T>(string key, out T asset) where T : Object
        {
            asset = null;
            if (string.IsNullOrEmpty(key))
                return false;
            if (!Cache.TryGetValue(NormalizeKey(key), out var obj) || obj == null)
                return false;
            asset = obj as T;
            return asset != null;
        }

        /// <summary>同步取：缓存优先；编辑器可直读。真机未加载过则 null。</summary>
        public static T Load<T>(string key) where T : Object
        {
            if (string.IsNullOrEmpty(key))
                return null;
            if (TryGetCached<T>(key, out var cached))
                return cached;
            if (Loader == null)
                return null;

            var loaded = Loader.LoadSync<T>(ToAssetPath<T>(key));
            if (loaded != null)
                Put(key, loaded);
            return loaded;
        }

        /// <summary>异步加载并写入缓存（主路径）。</summary>
        public static IEnumerator LoadAsync<T>(string key, Action<T> onDone) where T : Object
        {
            if (string.IsNullOrEmpty(key))
            {
                onDone?.Invoke(null);
                yield break;
            }

            if (TryGetCached<T>(key, out var cached))
            {
                onDone?.Invoke(cached);
                yield break;
            }

            if (Loader == null)
            {
                onDone?.Invoke(null);
                yield break;
            }

            Object raw = null;
            yield return Loader.LoadAsync(ToAssetPath<T>(key), typeof(T), o => raw = o);
            var typed = raw as T;
            if (typed != null)
            {
                Put(key, typed);
            }
            else if (raw != null)
            {
                Debug.LogWarning(
                    $"[BattleAssets] 类型不匹配 key={key} want={typeof(T).Name} got={raw.GetType().Name}");
            }
            onDone?.Invoke(typed);
        }

        /// <summary>批量异步加载并写入缓存。配置表用 <see cref="TextAsset"/>，暖场 Prefab 用 <see cref="GameObject"/>。</summary>
        public static IEnumerator LoadAllAsync<T>(IList<string> keys) where T : Object
        {
            if (Loader == null || keys == null || keys.Count == 0)
                yield break;

            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (string.IsNullOrEmpty(key) || TryGetCached<T>(key, out _))
                    continue;
                yield return LoadAsync<T>(key, null);
            }
        }

        /// <summary>批量异步加载 Prefab（暖场表与业务 key 相同）。</summary>
        public static IEnumerator LoadAllAsync(IList<string> keys)
        {
            yield return LoadAllAsync<GameObject>(keys);
        }

        /// <summary>可选暖场：把 key 提前装进缓存。空数组直接结束。</summary>
        public static IEnumerator PreloadAsync(IList<string> keys)
        {
            yield return LoadAllAsync<GameObject>(keys);
        }

        public static string NormalizeKey(string key)
        {
            return key.Replace('\\', '/').Trim('/');
        }

        /// <summary>相对 key → <c>Assets/Game/...</c>。已有扩展名或已是 Assets/ 路径则原样使用。</summary>
        public static string ToAssetPath<T>(string key)
        {
            string rel = NormalizeKey(key);
            if (string.IsNullOrEmpty(rel))
                return rel;
            if (rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return rel;

            string path = BattlePaths.GameRoot + "/" + rel;
            if (!string.IsNullOrEmpty(Path.GetExtension(rel)))
                return path;

            string ext = ExtensionFor(typeof(T));
            return ext.Length == 0 ? path : path + ext;
        }

        static string ExtensionFor(Type t)
        {
            if (t == typeof(GameObject)) return ".prefab";
            if (t == typeof(Material)) return ".mat";
            if (t == typeof(AudioClip)) return ".wav";
            if (t == typeof(Font)) return ".ttf";
            if (t == typeof(Texture2D) || t == typeof(Sprite)) return ".png";
            if (t == typeof(RuntimeAnimatorController)) return ".controller";
            if (t == typeof(AnimationClip)) return ".anim";
            if (t == typeof(TextAsset)) return ".json";
            return "";
        }
    }

    public static class BattlePlaceholderKeys
    {
        public const string Default = "Prefabs/Battle/BattlePlaceholder";
    }

    public sealed class ZeonAssetBattleAssetLoader : IBattleAssetLoader
    {
        public static readonly ZeonAssetBattleAssetLoader Instance = new ZeonAssetBattleAssetLoader();

        public T LoadSync<T>(string location) where T : Object
        {
            return BattleAssets.TryGetCached<T>(location, out var cached) ? cached : null;
        }

        public IEnumerator LoadAsync(string location, Type assetType, Action<Object> onDone)
        {
            if (string.IsNullOrEmpty(location) || !AssetManager.IsInitialized)
            {
                onDone?.Invoke(null);
                yield break;
            }

            var type = assetType ?? typeof(Object);
            var op = AssetManager.LoadAssetAsync(location, type);
            while (op != null && !op.IsDone)
            {
                OperationSystem.Update();
                BundleLoaderManager.Update();
                FileDownloader.Update();
                yield return null;
            }

            Object asset = null;
            if (op != null && op.IsSucceed && op.Result != null)
                asset = op.Result.AssetObject;
            if (asset == null)
                Debug.LogWarning("[BattleAssets] 加载失败: " + location + " type=" + type.Name);
            onDone?.Invoke(asset);
        }
    }

#if UNITY_EDITOR
    public sealed class EditorAssetDatabaseBattleAssetLoader : IBattleAssetLoader
    {
        public static readonly EditorAssetDatabaseBattleAssetLoader Instance =
            new EditorAssetDatabaseBattleAssetLoader();

        public T LoadSync<T>(string location) where T : Object
        {
            if (string.IsNullOrEmpty(location))
                return null;
            if (BattleAssets.TryGetCached<T>(location, out var cached))
                return cached;
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(location);
        }

        public IEnumerator LoadAsync(string location, Type assetType, Action<Object> onDone)
        {
            Object asset = null;
            if (!string.IsNullOrEmpty(location))
            {
                var type = assetType ?? typeof(Object);
                if (BattleAssets.TryGetCached<Object>(location, out var cached) && type.IsInstanceOfType(cached))
                    asset = cached;
                else
                    asset = UnityEditor.AssetDatabase.LoadAssetAtPath(location, type);
            }

            onDone?.Invoke(asset);
            yield break;
        }
    }
#endif
}
