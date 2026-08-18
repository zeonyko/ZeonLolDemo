using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>按 key 加载战斗短音效；播放走场景里的 SfxPlayer Prefab 声部池。</summary>
    public static class SfxLibrary
    {
        public const string ClipFolder = "Audio/Sfx";
        public const string PlayerPrefabKey = "Prefabs/Audio/SfxPlayer";

        static readonly Dictionary<string, AudioClip> Cache =
            new Dictionary<string, AudioClip>(16, StringComparer.OrdinalIgnoreCase);

        static int _spawnEpoch;

        public static string NormalizeKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            key = key.Trim();
            if (!key.StartsWith("Sfx_", StringComparison.OrdinalIgnoreCase))
                key = "Sfx_" + key;
            return key;
        }

        public static IEnumerator EnsureAsync()
        {
            yield return SfxPlayer.EnsureAsync();
        }

        public static IEnumerator EnsureClipAsync(string key, Action<AudioClip> onDone)
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

            AudioClip clip = null;
            yield return BattleAssets.LoadAsync<AudioClip>($"{ClipFolder}/{norm}", c => clip = c);
            if (clip != null)
                Cache[norm] = clip;
            else
                Debug.LogWarning($"[SfxLibrary] Missing clip {ClipFolder}/{norm}");
            onDone?.Invoke(clip);
        }

        /// <summary>已缓存则立刻播；否则按需装播放器和 clip。</summary>
        public static void Play(string key, Vector3 worldPos, float volume = 1f)
        {
            string norm = NormalizeKey(key);
            if (string.IsNullOrEmpty(norm)) return;

            if (SfxPlayer.Instance != null
                && Cache.TryGetValue(norm, out var cached) && cached != null)
            {
                SfxPlayer.Play(cached, worldPos, volume);
                return;
            }

            int epoch = _spawnEpoch;
            BattleAssets.Run(CoPlayWhenReady(epoch, norm, worldPos, volume));
        }

        static IEnumerator CoPlayWhenReady(int epoch, string norm, Vector3 worldPos, float volume)
        {
            yield return SfxPlayer.EnsureAsync();
            AudioClip clip = null;
            yield return EnsureClipAsync(norm, c => clip = c);
            if (epoch != _spawnEpoch || clip == null) yield break;
            SfxPlayer.Play(clip, worldPos, volume);
        }

        public static void Clear()
        {
            _spawnEpoch++;
            Cache.Clear();
            SfxPlayer.Shutdown();
        }
    }
}
