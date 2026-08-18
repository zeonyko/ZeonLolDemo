using UnityEngine;

namespace Launch
{
    /// <summary>
    /// 防止 Strip Engine Code 裁掉「只在热更 AB / 热更 DLL 里出现」的引擎类型。
    /// 典型报错：Could not produce class with ID 135（SphereCollider）。
    /// Game.HotUpdate 里的 CreatePrimitive 对 IL2CPP 裁剪不可见，必须在 AOT（Game.Launch）里钉死引用。
    /// </summary>
    public static class EngineCodePreserve
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void KeepForAssetBundles()
        {
            // typeof 钉住托管类型；CreatePrimitive 钉住原生引擎类（Strip Engine Code）
            KeepPrimitive(PrimitiveType.Sphere);
            KeepPrimitive(PrimitiveType.Capsule);
            KeepPrimitive(PrimitiveType.Cube);
            KeepPrimitive(PrimitiveType.Cylinder);
            KeepPrimitive(PrimitiveType.Plane);
            KeepPrimitive(PrimitiveType.Quad);

            Keep<BoxCollider>();
            Keep<MeshCollider>();
            Keep<Rigidbody>();
            Keep<PhysicMaterial>();

            Keep<SkinnedMeshRenderer>();
            Keep<Animator>();
            Keep<AnimationClip>();

            Keep<LODGroup>();
            Keep<TrailRenderer>();
            Keep<LineRenderer>();
            Keep<ParticleSystem>();
            Keep<ParticleSystemRenderer>();
            Keep<AudioSource>();
            Keep<Light>();
        }

        static void KeepPrimitive(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go);
        }

        static void Keep<T>() where T : Object
        {
            _ = typeof(T);
        }
    }
}
