using Client.SkillAuthoring;
using UnityEditor;

namespace Client.EditorTools
{
    /// <summary>技能 / 弹道 / Buff / 受击：一键从 Config 同步，或导回 Config 再分发到双端。</summary>
    public static class ConfigBridgeMenus
    {
        [MenuItem("Game/Skill/从 Config 同步全部", false, 90)]
        public static void SyncAllFromConfig()
        {
            int skills = SkillExporter.SyncAssetsFromJson(reExport: false);
            int projectiles = ProjectileExporter.SyncAssetsFromJson();
            int buffs = BuffExporter.SyncAssetsFromJson();
            int reactions = SkillReactionExporter.SyncFromJson();
            EditorUtility.DisplayDialog(
                "从 Config 同步",
                $"技能 {skills}\n弹道 {projectiles}\nBuff {buffs}\n受击/施法反馈 {reactions}\n\n" +
                "资源在 Assets/Game/SkillData。权威源仍是仓库 Config/。",
                "OK");
        }

        [MenuItem("Game/Skill/导出全部到 Config", false, 91)]
        public static void ExportAllToConfig()
        {
            int skills = ExportPipeline.ExportAllAssets<SkillAsset>(
                SkillExporter.SkillsDir, "t:SkillAsset",
                a => SkillExporter.ExportAsset(a, out _));
            int projectiles = ProjectileExporter.ExportAll();
            int buffs = BuffExporter.ExportAll();
            int reactions = SkillReactionExporter.ExportAll();

            bool synced = ExportPipeline.SyncRuntimeCopies(out string syncErr);
            string syncLine = synced
                ? "已同步到 Client/Assets/Game/Configs 与 Server/Config。"
                : "Config 已写入，但运行时副本同步失败：\n" + (syncErr ?? "");

            EditorUtility.DisplayDialog(
                "导出到 Config",
                $"技能 {skills}\n弹道 {projectiles}\nBuff {buffs}\n受击/施法反馈 {reactions}\n\n{syncLine}",
                "OK");
        }
    }
}
