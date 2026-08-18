using System.Collections;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗音效播放器：Prefab 上预置声部，禁止运行时 AddComponent。</summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        public const string PrefabKey = SfxLibrary.PlayerPrefabKey;

        public static SfxPlayer Instance { get; private set; }

        [SerializeField] AudioSource[] voices;

        int _cursor;

        public static IEnumerator EnsureAsync()
        {
            if (Instance != null) yield break;

            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(PrefabKey, p => prefab = p);
            if (prefab == null)
            {
                Debug.LogWarning("[SfxPlayer] 缺少 Prefabs/Audio/SfxPlayer");
                yield break;
            }

            FinishInstantiate(prefab);
        }

        static void FinishInstantiate(GameObject prefab)
        {
            if (Instance != null || prefab == null) return;
            var parent = BattleScene.Vfx;
            var go = Object.Instantiate(prefab, parent);
            go.name = "SfxPlayer";
            Instance = go.GetComponent<SfxPlayer>();
            if (Instance == null)
                Debug.LogWarning("[SfxPlayer] Prefab 未挂 SfxPlayer 脚本");
        }

        public static void Play(AudioClip clip, Vector3 worldPos, float volume = 1f)
        {
            if (clip == null || Instance == null) return;
            Instance.PlayInternal(clip, worldPos, volume);
        }

        public static void Shutdown()
        {
            if (Instance == null) return;
            if (Instance.gameObject != null)
                Object.Destroy(Instance.gameObject);
            Instance = null;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (voices == null || voices.Length == 0)
                voices = GetComponentsInChildren<AudioSource>(true);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void PlayInternal(AudioClip clip, Vector3 worldPos, float volume)
        {
            if (voices == null || voices.Length == 0) return;
            var src = voices[_cursor % voices.Length];
            _cursor++;
            if (src == null) return;
            src.transform.position = worldPos;
            src.spatialBlend = 0f;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}
