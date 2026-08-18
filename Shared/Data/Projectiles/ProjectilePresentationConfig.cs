using System;

namespace Shared
{
    /// <summary>Projectile_{id}.json 根。逻辑两端都要，表现只给客户端。</summary>
    [Serializable]
    public class ProjectileConfigFile
    {
        public ProjectileConfig Logic;
        public ProjectilePresentationConfig Presentation;
    }

    /// <summary>弹道表现段：飞行特效键和颜色。服务器不读。</summary>
    [Serializable]
    public class ProjectilePresentationConfig
    {
        public int ProjectileId;
        public string VfxKey = "";
        public float VfxColorR = 0.9f;
        public float VfxColorG = 0.9f;
        public float VfxColorB = 1f;
        public float VfxColorA = 0.9f;
        public float VfxScaleX = 0.4f;
        public float VfxScaleY = 0.4f;
        public float VfxYLift;
    }

    /// <summary>客户端弹道表现表。服务器通常不加载。</summary>
    public static class ProjectilePresentationCatalog
    {
        static readonly IdCatalog<ProjectilePresentationConfig> Items = new IdCatalog<ProjectilePresentationConfig>(
            c => c.ProjectileId,
            c =>
            {
                if (string.IsNullOrEmpty(c.VfxKey))
                    c.VfxKey = "";
            });

        public static void RegisterOrReplace(ProjectilePresentationConfig data) => Items.RegisterOrReplace(data);
        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<ProjectilePresentationConfig> list) =>
            Items.ClearAndLoad(list);
        public static ProjectilePresentationConfig Get(int projectileId) => Items.Get(projectileId);
        public static bool TryGet(int projectileId, out ProjectilePresentationConfig data) =>
            Items.TryGet(projectileId, out data);

        public static bool TryGetByVfxKey(string vfxKey, out ProjectilePresentationConfig data)
        {
            data = null;
            if (string.IsNullOrEmpty(vfxKey)) return false;
            return Items.TryFind(
                c => string.Equals(c.VfxKey, vfxKey, StringComparison.OrdinalIgnoreCase),
                out data);
        }

        public static System.Collections.Generic.IReadOnlyDictionary<int, ProjectilePresentationConfig> All => Items.All;
    }
}
