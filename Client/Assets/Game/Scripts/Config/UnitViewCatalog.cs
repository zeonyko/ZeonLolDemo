using System;
using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>客户端单位外观表。PrefabKey 只在这里，不进服务端 UnitProfiles。</summary>
    [Serializable]
    public class UnitViewProfile
    {
        public int EntityType;
        public string PrefabKey = "";
    }

    [Serializable]
    public class UnitViewProfileConfig
    {
        public UnitViewProfile[] Views = Array.Empty<UnitViewProfile>();
    }

    public static class UnitViewCatalog
    {
        static readonly Dictionary<int, string> Map = new Dictionary<int, string>();

        public static int Count => Map.Count;

        public static void ClearAndLoad(UnitViewProfileConfig file)
        {
            Map.Clear();
            if (file?.Views == null)
                return;

            for (int i = 0; i < file.Views.Length; i++)
            {
                var view = file.Views[i];
                if (view == null || view.EntityType <= 0 || string.IsNullOrWhiteSpace(view.PrefabKey))
                    continue;
                Map[view.EntityType] = view.PrefabKey.Trim();
            }
        }

        public static void Clear() => Map.Clear();

        public static string GetPrefabKey(int entityType)
        {
            Map.TryGetValue(entityType, out var key);
            return key;
        }

        public static void LoadFromConfig()
        {
            if (!ConfigService.Exists("Units", "UnitViewProfiles.json"))
            {
                Clear();
                Debug.LogWarning("[UnitViewCatalog] 未找到 Units/UnitViewProfiles.json，单位将使用占位 Prefab");
                return;
            }

            var file = ConfigService.LoadFile<UnitViewProfileConfig>("Units", "UnitViewProfiles.json");
            ClearAndLoad(file);
            Debug.Log($"[UnitViewCatalog] loaded={Map.Count}");
        }
    }
}
