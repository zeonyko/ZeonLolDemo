using UnityEngine;

namespace Client.Battle
{
    /// <summary>场景里的地图根。退出战斗时留着地图，只清角色和特效。</summary>
    public sealed class AuthoredMapRoot : MonoBehaviour
    {
        [Tooltip("退出战斗时留着地图，只清角色和特效")]
        public bool PreserveOnShutdown = true;

        public static AuthoredMapRoot Find() => Object.FindObjectOfType<AuthoredMapRoot>();

        public static bool TryFind(out AuthoredMapRoot root)
        {
            root = Find();
            return root != null;
        }
    }
}
