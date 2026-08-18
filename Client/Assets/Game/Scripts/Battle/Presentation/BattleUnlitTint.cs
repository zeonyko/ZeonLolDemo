using System.Collections;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>
    /// 战斗纯色 tint：材质用时 LoadAsync；不 Shader.Find、不运行时拼材质。
    /// </summary>
    public static class BattleUnlitTint
    {
        public const string MaterialKey = "Materials/mat_battle_unlit_color";

        static Material _shared;
        static MaterialPropertyBlock _block;
        static bool _loading;
        static bool _loggedMissing;

        public static Material Shared => _shared;

        public static void Apply(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;
            if (_shared != null)
            {
                Bind(renderer, color);
                return;
            }

            EnsureLoaded(() =>
            {
                if (renderer != null)
                    Bind(renderer, color);
            });
        }

        public static void SetColor(Renderer renderer, Color color)
        {
            if (renderer == null || _shared == null)
                return;
            if (_block == null)
                _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor("_Color", color);
            renderer.SetPropertyBlock(_block);
        }

        public static void Apply(LineRenderer line, Color color)
        {
            if (line == null)
                return;
            if (_shared != null)
            {
                Bind(line, color);
                return;
            }

            EnsureLoaded(() =>
            {
                if (line != null)
                    Bind(line, color);
            });
        }

        static void Bind(Renderer renderer, Color color)
        {
            if (_shared == null) return;
            if (renderer.sharedMaterial != _shared)
                renderer.sharedMaterial = _shared;
            SetColor(renderer, color);
        }

        static void Bind(LineRenderer line, Color color)
        {
            if (_shared == null) return;
            if (line.sharedMaterial != _shared)
                line.sharedMaterial = _shared;
            if (_block == null)
                _block = new MaterialPropertyBlock();
            line.GetPropertyBlock(_block);
            _block.SetColor("_Color", color);
            line.SetPropertyBlock(_block);
            line.startColor = color;
            line.endColor = color;
        }

        static void EnsureLoaded(System.Action onReady)
        {
            if (_shared != null)
            {
                onReady?.Invoke();
                return;
            }

            var cached = BattleAssets.Load<Material>(MaterialKey);
            if (cached != null)
            {
                _shared = cached;
                onReady?.Invoke();
                return;
            }

            if (_loading)
            {
                BattleAssets.Run(WaitThen(onReady));
                return;
            }

            _loading = true;
            BattleAssets.Run(CoLoad(onReady));
        }

        static IEnumerator WaitThen(System.Action onReady)
        {
            while (_loading && _shared == null)
                yield return null;
            onReady?.Invoke();
        }

        static IEnumerator CoLoad(System.Action onReady)
        {
            yield return BattleAssets.LoadAsync<Material>(MaterialKey, m => _shared = m);
            _loading = false;
            if (_shared == null && !_loggedMissing)
            {
                _loggedMissing = true;
                Debug.LogError("[Battle] 缺少材质 " + MaterialKey);
            }

            onReady?.Invoke();
        }
    }
}
