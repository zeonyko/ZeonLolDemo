using System.IO;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>
    /// 战斗工程路径约定。
    /// - 权威配置：仓库 Config/（与 Client、Server 同级），只改这里。
    /// - 运行时读取：Client/Assets/Game/Configs（Tools/sync_config-同步配置表.bat 生成）。
    /// - 技能导出 / 地图导出：写入 RepoConfigRoot，再同步。
    /// </summary>
    public static class BattlePaths
    {
        public const string GameRoot = "Assets/Game";
        public const string ConfigsAsset = "Assets/Game/Configs";
        public const string SkillData = "Assets/Game/SkillData";

        /// <summary>仓库根 Config/ 绝对路径（权威源）。</summary>
        public static string RepoConfigRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Config"));

        /// <summary>客户端运行时读取目录（同步产物，勿手改）。</summary>
        public static string ConfigRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "Game", "Configs"));

        /// <summary>编辑器写权威配置（导出技能 / Buff / 地图等）。</summary>
        public static string RepoConfig(params string[] relativeParts) =>
            Combine(RepoConfigRoot, relativeParts);

        /// <summary>RepoConfig 简写，导出器常用。</summary>
        public static string Config(params string[] relativeParts) =>
            RepoConfig(relativeParts);

        static string Combine(string root, string[] relativeParts)
        {
            if (relativeParts == null || relativeParts.Length == 0)
                return root;

            string[] all = new string[relativeParts.Length + 1];
            all[0] = root;
            for (int i = 0; i < relativeParts.Length; i++)
                all[i + 1] = relativeParts[i];
            return Path.GetFullPath(Path.Combine(all));
        }
    }
}
