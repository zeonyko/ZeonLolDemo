using System;
using System.Collections.Generic;
using Client.Battle;
using Client.SkillAuthoring;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Client.EditorTools
{
    /// <summary>弹道编辑资产 ↔ 配置表。服务端日常直接读这份；可选菜单再拷一份给 Server。</summary>
    public static class ProjectileExporter
    {
        #region 路径 & 初始化
        public const string ProjectilesDir = "Assets/Game/SkillData/Projectiles";
        public static string ClientJsonDir => BattlePaths.Config("Projectiles");

        public static void EnsureFolders() =>
            ExportPipeline.EnsureFolders(ProjectilesDir, ClientJsonDir);

        public static string AssetPath(int id) => $"{ProjectilesDir}/Projectile_{id}.asset";
        #endregion

        #region 导出：ProjectileAsset → Projectile_{id}.json
        /// <summary>拆开逻辑和表现，写入弹道配置表。</summary>
        public static bool ExportAsset(ProjectileAsset asset, out string error)
        {
            error = null;
            if (asset == null)
            {
                error = "ProjectileAsset is null";
                return false;
            }

            if (asset.ProjectileId == 0)
            {
                error = "ProjectileId 不能为 0";
                return false;
            }

            EnsureFolders();
            var logic = asset.ToData();
            var pres = asset.ToPresentation();
            string clientJson = JsonUtility.ToJson(new ProjectileConfigFile
            {
                Logic = logic,
                Presentation = pres
            }, true);

            ExportPipeline.WriteJson(ClientJsonDir, $"Projectile_{logic.ProjectileId}.json", clientJson);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>批量导出 ProjectilesDir 下全部 ProjectileAsset。</summary>
        public static int ExportAll() =>
            ExportPipeline.ExportAllAssets<ProjectileAsset>(ProjectilesDir, "t:ProjectileAsset",
                a => ExportAsset(a, out _));
        #endregion

        #region 新建 ProjectileAsset
        public static ProjectileAsset CreateOrReplace(int id, string name)
        {
            EnsureFolders();
            return ExportPipeline.CreateOrReplace<ProjectileAsset>(
                AssetPath(id),
                existing =>
                {
                    if (!string.IsNullOrEmpty(name))
                        existing.ProjectileName = name;
                },
                asset =>
                {
                    asset.ProjectileId = id;
                    asset.ProjectileName = string.IsNullOrEmpty(name) ? $"Projectile_{id}" : name;
                    asset.VfxKey = asset.ProjectileName;
                });
        }
        #endregion

        #region 反向同步：Projectile JSON → ProjectileAsset
        /// <summary>从 Configs JSON 同步/重建全部 ProjectileAsset。</summary>
        public static int SyncAssetsFromJson()
        {
            EnsureFolders();
            var loaded = new Dictionary<int, ProjectileConfig>();
            var presentations = new Dictionary<int, ProjectilePresentationConfig>();
            ExportPipeline.LoadJsonDir<ProjectileConfigFile, ProjectileConfig>(
                ExportPipeline.Abs(ClientJsonDir),
                "Projectile_*.json",
                file => file.Logic,
                (file, logic) =>
                {
                    if (file.Presentation != null)
                        presentations[logic.ProjectileId] = file.Presentation;
                },
                c => c.ProjectileId,
                loaded,
                "ProjectileSync");

            if (loaded.Count == 0)
            {
                Debug.LogWarning("[ProjectileSync] 未找到任何 Projectile_*.json");
                return 0;
            }

            int ok = 0;
            foreach (var kv in loaded)
            {
                var cfg = kv.Value;
                presentations.TryGetValue(kv.Key, out var pres);
                var asset = CreateOrReplace(cfg.ProjectileId, cfg.Name);
                asset.ApplyFromData(cfg, pres);
                EditorUtility.SetDirty(asset);
                ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProjectileSync] assets={ok}");
            return ok;
        }
        #endregion
    }

    /// <summary>ProjectileAsset 的 Inspector：默认字段 + 导出 JSON 按钮。</summary>
    [CustomEditor(typeof(ProjectileAsset))]
    public class ProjectileAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var asset = (ProjectileAsset)target;
            EditorGUILayout.Space(6);
            int bolts = Mathf.Max(1, asset.BoltCount);
            EditorGUILayout.HelpBox(
                bolts <= 1
                    ? "单弹直线。"
                    : $"齐射预览：{bolts} 箭，扇形 {asset.SpreadDegrees:F0}°（中心对准施法方向）。",
                MessageType.Info);

            GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
            if (GUILayout.Button("导出到 Configs", GUILayout.Height(28)))
            {
                if (ProjectileExporter.ExportAsset(asset, out string err))
                    EditorUtility.DisplayDialog("导出", $"Projectile_{asset.ProjectileId}", "OK");
                else
                    EditorUtility.DisplayDialog("导出", err, "OK");
            }
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>Game/Skill 菜单：创建弹道 / JSON↔Asset 同步 / 批量导出。</summary>
    public static class ProjectileMenus
    {
        [MenuItem("Game/Skill/创建弹道", false, 10)]
        public static void CreateNewProjectile()
        {
            ProjectileExporter.EnsureFolders();
            int id = 2100;
            while (AssetDatabase.LoadAssetAtPath<ProjectileAsset>(ProjectileExporter.AssetPath(id)) != null)
                id++;

            var asset = ProjectileExporter.CreateOrReplace(id, $"新弹道_{id}");
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Game/Skill/从 JSON 同步弹道", false, 11)]
        public static void SyncFromJson()
        {
            int ok = ProjectileExporter.SyncAssetsFromJson();
            EditorUtility.DisplayDialog(
                "同步弹道",
                $"已同步 {ok} 个 ProjectileAsset\n目录：{ProjectileExporter.ProjectilesDir}",
                "OK");
        }

        [MenuItem("Game/Skill/导出全部弹道 JSON", false, 12)]
        public static void ExportAll()
        {
            int ok = ProjectileExporter.ExportAll();
            EditorUtility.DisplayDialog("导出弹道", $"已导出 {ok} 个到 Configs", "OK");
        }

        /// <summary>供 -batchmode -executeMethod 调用。</summary>
        public static void SyncAssetsFromJsonBatch()
        {
            int ok = ProjectileExporter.SyncAssetsFromJson();
            if (ok <= 0)
                throw new Exception("[ProjectileSync] failed");
            Debug.Log($"[ProjectileSync] batch ok={ok}");
        }
    }
}
