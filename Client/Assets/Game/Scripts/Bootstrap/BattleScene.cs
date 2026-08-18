using UnityEngine;

namespace Client.Battle
{
    /// <summary>场景里的战斗根节点。地图、角色、特效在场景里摆好，这里只挂引用。</summary>
    public sealed class BattleScene : MonoBehaviour
    {
        static BattleScene _instance;

        [SerializeField] Transform map;
        [SerializeField] Transform entities;
        [SerializeField] Transform vfx;

        public static BattleScene Current
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<BattleScene>();
                return _instance;
            }
        }

        public static Transform Map => Current != null ? Current.map : null;
        public static Transform Entities => Current != null ? Current.entities : null;
        public static Transform Vfx => Current != null ? Current.vfx : null;

        void Awake() => _instance = this;

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>退出战斗：默认留着地图，只清角色和特效。</summary>
        public static void Shutdown()
        {
            var scene = Current;
            if (scene == null) return;

            bool preserve = AuthoredMapRoot.TryFind(out var authored) && authored.PreserveOnShutdown;
            if (preserve)
            {
                ClearChildren(scene.entities);
                ClearChildren(scene.vfx);
                return;
            }

            Destroy(scene.gameObject);
        }

        static void ClearChildren(Transform parent)
        {
            if (parent == null) return;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null)
                    Destroy(child.gameObject);
            }
        }
    }
}
