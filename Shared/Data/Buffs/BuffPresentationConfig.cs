using System;

namespace Shared
{
    /// <summary>Buff_{id}.json 根。逻辑两端都要，表现只给客户端。</summary>
    [Serializable]
    public class BuffConfigFile
    {
        public BuffConfig Logic;
        public BuffPresentationConfig Presentation;
    }

    /// <summary>Buff 表现段：循环特效、染色、挂点。服务器不读。</summary>
    [Serializable]
    public class BuffPresentationConfig
    {
        public int BuffId;
        public string LoopVfxKey = "";
        public int AttachPoint = (int)BuffAttachPoint.Foot;
        public float TintR = 1f;
        public float TintG = 1f;
        public float TintB = 1f;
        public float TintA = 1f;
        public float TintStrength;
        public float AnimSpeedMultiplier = 1f;
        public float LoopScale = 1f;
        public string PlaybackId = "";

        public BuffAttachPoint Attach
        {
            get => (BuffAttachPoint)AttachPoint;
            set => AttachPoint = (int)value;
        }
    }

    /// <summary>客户端 Buff 表现表。服务器通常不加载。</summary>
    public static class BuffPresentationCatalog
    {
        static readonly IdCatalog<BuffPresentationConfig> Items = new IdCatalog<BuffPresentationConfig>(
            c => c.BuffId,
            c =>
            {
                if (c.AnimSpeedMultiplier <= 0f)
                    c.AnimSpeedMultiplier = 1f;
                if (c.LoopScale <= 0.01f)
                    c.LoopScale = 1f;
            });

        public static void RegisterOrReplace(BuffPresentationConfig data) => Items.RegisterOrReplace(data);
        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<BuffPresentationConfig> list) =>
            Items.ClearAndLoad(list);
        public static BuffPresentationConfig Get(int buffId) => Items.Get(buffId);
        public static bool TryGet(int buffId, out BuffPresentationConfig data) => Items.TryGet(buffId, out data);
        public static System.Collections.Generic.IReadOnlyDictionary<int, BuffPresentationConfig> All => Items.All;
    }
}
