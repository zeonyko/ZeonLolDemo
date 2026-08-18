using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 零引用延迟卸载队列。
    /// RefCount==0 时不立刻 Unload，静置一段时间；期间再次 Retain 则唤醒。
    /// </summary>
    public static class UnloadDelayQueue
    {
        private struct Entry
        {
            public BundleLoader Loader;
            public float ExpireTime;
        }

        private static readonly List<Entry> Queue = new List<Entry>(64);
        private static readonly HashSet<BundleLoader> InQueue = new HashSet<BundleLoader>();

        public static int Count => Queue.Count;

        public static void Enqueue(BundleLoader loader)
        {
            if (loader == null)
                return;

            float delay = AssetManager.Config != null
                ? Mathf.Max(0f, AssetManager.Config.UnloadDelaySeconds)
                : 10f;

            float expire = Time.realtimeSinceStartup + delay;

            if (InQueue.Contains(loader))
            {
                for (int i = 0; i < Queue.Count; i++)
                {
                    if (ReferenceEquals(Queue[i].Loader, loader))
                    {
                        Queue[i] = new Entry { Loader = loader, ExpireTime = expire };
                        return;
                    }
                }
            }

            InQueue.Add(loader);
            Queue.Add(new Entry { Loader = loader, ExpireTime = expire });
        }

        public static void Cancel(BundleLoader loader)
        {
            if (loader == null || !InQueue.Contains(loader))
                return;

            InQueue.Remove(loader);
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(Queue[i].Loader, loader))
                    Queue.RemoveAt(i);
            }
        }

        public static bool Contains(BundleLoader loader)
        {
            return loader != null && InQueue.Contains(loader);
        }

        public static void Update()
        {
            if (Queue.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                var entry = Queue[i];
                if (entry.Loader == null)
                {
                    Queue.RemoveAt(i);
                    continue;
                }

                // 期间又被引用，防御性取消
                if (entry.Loader.RefCount > 0)
                {
                    InQueue.Remove(entry.Loader);
                    Queue.RemoveAt(i);
                    continue;
                }

                if (now < entry.ExpireTime)
                    continue;

                InQueue.Remove(entry.Loader);
                Queue.RemoveAt(i);
                entry.Loader.ExecuteUnload();
            }
        }

        /// <summary>内存告警 / 主动清理：立刻卸载队列中全部。</summary>
        public static void ForceUnloadAll()
        {
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                var loader = Queue[i].Loader;
                if (loader != null && loader.RefCount <= 0)
                    loader.ExecuteUnload();
            }

            Queue.Clear();
            InQueue.Clear();
        }

        public static void Clear()
        {
            Queue.Clear();
            InQueue.Clear();
        }
    }
}
