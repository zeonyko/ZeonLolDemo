using System;

namespace Shared
{
    /// <summary>Skill_{id}.json 根。逻辑两端都要，表现只给客户端。</summary>
    [Serializable]
    public class SkillConfigFile
    {
        public SkillConfig Logic;
        public SkillPresentationConfig Presentation;
    }

    /// <summary>技能表现段：动画、特效、镜头，不参与命中计算。</summary>
    [Serializable]
    public class SkillPresentationConfig
    {
        public int SkillId;
        /// <summary>动画 / 音效 / 特效 / 镜头 / 顿帧轨。</summary>
        public SkillClip[] Clips = Array.Empty<SkillClip>();
        public string CastVfxModule = "";
        public string HitImpactModule = "";
        /// <summary>没有动画轨时用的默认动画名。</summary>
        public string DefaultAnimKey = "";
        /// <summary>不播英雄脚下爆发/闪白。</summary>
        public bool SuppressCastBurst;
    }

    public static class SkillCastVfxModules
    {
        public const string TowerLockBeam = "TowerLockBeam";
    }

    public static class SkillHitImpactModules
    {
        public const string TowerImpact = "TowerImpact";
    }

    /// <summary>客户端技能表现表。服务器通常不加载。</summary>
    public static class SkillPresentationCatalog
    {
        static readonly IdCatalog<SkillPresentationConfig> Items = new IdCatalog<SkillPresentationConfig>(
            c => c.SkillId,
            c =>
            {
                if (c.Clips == null) c.Clips = Array.Empty<SkillClip>();
                LogicPresentationSplit.NormalizePresentationClips(c.Clips);
            });

        public static void RegisterOrReplace(SkillPresentationConfig data) => Items.RegisterOrReplace(data);
        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<SkillPresentationConfig> list) =>
            Items.ClearAndLoad(list);
        public static SkillPresentationConfig Get(int skillId) => Items.Get(skillId);
        public static bool TryGet(int skillId, out SkillPresentationConfig data) => Items.TryGet(skillId, out data);
        public static System.Collections.Generic.IReadOnlyDictionary<int, SkillPresentationConfig> All => Items.All;
    }
}
