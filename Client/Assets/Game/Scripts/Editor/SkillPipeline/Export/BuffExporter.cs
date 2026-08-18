using System.Collections.Generic;
using Client.Battle;
using Client.SkillAuthoring;
using Shared;
using UnityEditor;
using UnityEngine;

namespace Client.EditorTools
{
    /// <summary>Buff 编辑资产 ↔ 配置表。服务端日常直接读这份；可选菜单再拷一份给 Server。</summary>
    public static class BuffExporter
    {
        #region 路径 & 初始化
        public const string BuffsDir = "Assets/Game/SkillData/Buffs";
        public static string ClientJsonDir => BattlePaths.Config("Buffs");

        public static void EnsureFolders() =>
            ExportPipeline.EnsureFolders(BuffsDir, ClientJsonDir);

        public static string AssetPath(int id) => $"{BuffsDir}/Buff_{id}.asset";
        #endregion

        #region 导出：BuffAsset → Buff_{id}.json
        /// <summary>拆开逻辑和表现，写入 Buff 配置表。</summary>
        public static bool ExportAsset(BuffAsset asset, out string error)
        {
            error = null;
            if (asset == null)
            {
                error = "BuffAsset is null";
                return false;
            }

            if (asset.BuffId == 0)
            {
                error = "BuffId 不能为 0";
                return false;
            }

            EnsureFolders();
            var logic = asset.ToData();
            var pres = asset.ToPresentation();
            string clientJson = JsonUtility.ToJson(new BuffConfigFile
            {
                Logic = logic,
                Presentation = pres
            }, true);

            ExportPipeline.WriteJson(ClientJsonDir, $"Buff_{logic.BuffId}.json", clientJson);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>批量导出 BuffsDir 下全部 BuffAsset。</summary>
        public static int ExportAll() =>
            ExportPipeline.ExportAllAssets<BuffAsset>(BuffsDir, "t:BuffAsset",
                a => ExportAsset(a, out _));
        #endregion

        #region 新建 / 更新 BuffAsset
        public static BuffAsset CreateOrReplace(int id, string name)
        {
            EnsureFolders();
            return ExportPipeline.CreateOrReplace<BuffAsset>(
                AssetPath(id),
                existing =>
                {
                    if (!string.IsNullOrEmpty(name))
                        existing.BuffName = name;
                },
                asset =>
                {
                    asset.BuffId = id;
                    asset.BuffName = string.IsNullOrEmpty(name) ? $"Buff_{id}" : name;
                });
        }

        public static BuffAsset UpsertFromConfig(BuffConfig cfg, BuffPresentationConfig presentation = null)
        {
            if (cfg == null || cfg.BuffId == 0) return null;
            var asset = CreateOrReplace(cfg.BuffId, cfg.Name);
            asset.ApplyFromData(cfg, presentation);
            EditorUtility.SetDirty(asset);
            return asset;
        }
        #endregion

        #region 反向同步：Buff JSON → BuffAsset
        public static int SyncAssetsFromJson()
        {
            EnsureFolders();
            var loaded = new Dictionary<int, BuffConfig>();
            var presentations = new Dictionary<int, BuffPresentationConfig>();
            ExportPipeline.LoadJsonDir<BuffConfigFile, BuffConfig>(
                ExportPipeline.Abs(ClientJsonDir),
                "Buff_*.json",
                file => file.Logic,
                (file, logic) =>
                {
                    if (file.Presentation != null)
                        presentations[logic.BuffId] = file.Presentation;
                },
                c => c.BuffId,
                loaded,
                "BuffSync");

            if (loaded.Count == 0)
            {
                Debug.LogWarning("[BuffSync] 未找到 Buff_*.json");
                return 0;
            }

            int ok = 0;
            foreach (var kv in loaded)
            {
                presentations.TryGetValue(kv.Key, out var pres);
                if (UpsertFromConfig(kv.Value, pres) != null)
                    ok++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ok;
        }
        #endregion
    }

    /// <summary>BuffAsset 的 Inspector：默认字段 + 导出 JSON 按钮。</summary>
    [CustomEditor(typeof(BuffAsset))]
    public class BuffAssetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var asset = (BuffAsset)target;
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                $"StatusFlags={asset.StatusFlags}  Mods={asset.AttributeModifiers?.Count ?? 0}\n" +
                "配置阶段资源；运行时 Catalog / 施加逻辑后续接入。",
                MessageType.Info);

            GUI.backgroundColor = new Color(0.45f, 0.85f, 0.55f);
            if (GUILayout.Button("导出到 Configs", GUILayout.Height(28)))
            {
                if (BuffExporter.ExportAsset(asset, out string err))
                    EditorUtility.DisplayDialog("导出", $"Buff_{asset.BuffId}", "OK");
                else
                    EditorUtility.DisplayDialog("导出", err, "OK");
            }
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>Game/Skill 菜单：创建 Buff / JSON↔Asset 同步 / 批量导出。</summary>
    public static class BuffMenus
    {
        [MenuItem("Game/Skill/创建 Buff", false, 20)]
        public static void CreateNewBuff()
        {
            BuffExporter.EnsureFolders();
            int id = 3100;
            while (AssetDatabase.LoadAssetAtPath<BuffAsset>(BuffExporter.AssetPath(id)) != null)
                id++;

            var asset = BuffExporter.CreateOrReplace(id, $"新Buff_{id}");
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        [MenuItem("Game/Skill/从 JSON 同步 Buff", false, 21)]
        public static void SyncFromJson()
        {
            int ok = BuffExporter.SyncAssetsFromJson();
            EditorUtility.DisplayDialog("同步 Buff", $"已同步 {ok} 个\n{BuffExporter.BuffsDir}", "OK");
        }

        [MenuItem("Game/Skill/导出全部 Buff JSON", false, 22)]
        public static void ExportAll()
        {
            int ok = BuffExporter.ExportAll();
            EditorUtility.DisplayDialog("导出 Buff", $"已导出 {ok} 个到 Configs", "OK");
        }
    }
}
