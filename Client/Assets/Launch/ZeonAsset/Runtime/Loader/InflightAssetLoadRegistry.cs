using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 同 PathID 和 Type 的加载请求合并。
    /// 主 Operation 真正加载；跟随者等待完成后各自创建 Handle。
    /// </summary>
    public static class InflightAssetLoadRegistry
    {
        private sealed class Entry
        {
            public LoadAssetOperation Primary;
            public readonly List<LoadAssetOperation> Followers = new List<LoadAssetOperation>(4);
        }

        private static readonly Dictionary<long, Entry> Map = new Dictionary<long, Entry>();

        public static long MakeKey(string location, Type assetType)
        {
            long pathId = PathId.Get(location);
            int typeHash = (assetType ?? typeof(UnityEngine.Object)).GetHashCode();
            unchecked
            {
                return pathId ^ ((long)typeHash << 1);
            }
        }

        public static bool TryGetPrimary(long key, out LoadAssetOperation primary)
        {
            if (Map.TryGetValue(key, out var entry) && entry.Primary != null && !entry.Primary.IsDone)
            {
                primary = entry.Primary;
                return true;
            }

            primary = null;
            return false;
        }

        public static void RegisterPrimary(long key, LoadAssetOperation primary)
        {
            Map[key] = new Entry { Primary = primary };
        }

        public static void AttachFollower(long key, LoadAssetOperation follower)
        {
            if (!Map.TryGetValue(key, out var entry) || entry.Primary == null)
            {
                ZeonAssetLog.Warn("AttachFollower failed: primary missing.");
                return;
            }

            entry.Followers.Add(follower);
        }

        /// <summary>主任务结束：完成所有跟随者并移除登记。</summary>
        public static void NotifyPrimaryFinished(long key, LoadAssetOperation primary)
        {
            if (!Map.TryGetValue(key, out var entry))
                return;

            if (!ReferenceEquals(entry.Primary, primary))
                return;

            Map.Remove(key);

            for (int i = 0; i < entry.Followers.Count; i++)
            {
                var follower = entry.Followers[i];
                if (follower == null || follower.IsDone)
                    continue;
                follower.CompleteAsFollower(primary);
            }

            entry.Followers.Clear();
        }

        public static void Clear()
        {
            Map.Clear();
        }
    }
}
