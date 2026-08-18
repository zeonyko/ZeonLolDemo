using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 异步操作调度器：每帧驱动 Processing 状态的 Operation。
    /// </summary>
    public static class OperationSystem
    {
        private static readonly List<AsyncOperationBase> _operations = new List<AsyncOperationBase>(64);
        private static readonly List<AsyncOperationBase> _adding = new List<AsyncOperationBase>(16);
        private static List<AsyncOperationBase> _recyclePrev = new List<AsyncOperationBase>(32);
        private static List<AsyncOperationBase> _recycleCurr = new List<AsyncOperationBase>(32);
        private static bool _driverCreated;

        public static void EnsureDriver()
        {
            if (_driverCreated)
                return;

            var go = new GameObject("[Resource.Driver]");
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.DontSave;
            go.AddComponent<ZeonAssetDriver>();
            _driverCreated = true;
        }

        public static void Start(AsyncOperationBase operation)
        {
            if (operation == null)
                return;

            EnsureDriver();
            _adding.Add(operation);
            operation.Start();
        }

        public static void Update()
        {
            if (_adding.Count > 0)
            {
                _operations.AddRange(_adding);
                _adding.Clear();
            }

            _recycleCurr.Clear();
            for (int i = _operations.Count - 1; i >= 0; i--)
            {
                var op = _operations[i];
                op.Update();
                if (op.IsDone)
                {
                    _operations.RemoveAt(i);
                    _recycleCurr.Add(op);
                }
            }
        }

        /// <summary>
        /// 还池：完成当帧的下一帧 LateUpdate。业务请在 IsDone 当帧或下一帧 Update 读取 Result。
        /// </summary>
        internal static void RecycleDeferred()
        {
            for (int i = 0; i < _recyclePrev.Count; i++)
                _recyclePrev[i].TryReturnToPool();
            _recyclePrev.Clear();

            var swap = _recyclePrev;
            _recyclePrev = _recycleCurr;
            _recycleCurr = swap;
        }

        public static void Clear()
        {
            ReturnAll(_operations);
            ReturnAll(_adding);
            ReturnAll(_recyclePrev);
            ReturnAll(_recycleCurr);
        }

        private static void ReturnAll(List<AsyncOperationBase> list)
        {
            for (int i = 0; i < list.Count; i++)
                list[i].TryReturnToPool();
            list.Clear();
        }
    }

    /// <summary>
    /// 隐藏驱动器：驱动 Operation / BundleLoader / 延迟卸载，并响应低内存。
    /// </summary>
    internal sealed class ZeonAssetDriver : MonoBehaviour
    {
        private void OnEnable()
        {
            Application.lowMemory += OnLowMemory;
        }

        private void OnDisable()
        {
            Application.lowMemory -= OnLowMemory;
        }

        private void Update()
        {
            OperationSystem.Update();
            BundleLoaderManager.Update();
            FileDownloader.Update();
        }

        private void LateUpdate()
        {
            OperationSystem.RecycleDeferred();
        }

        private void OnLowMemory()
        {
            ZeonAssetLog.Warn("lowMemory: force unload delay queue + UnloadUnusedAssets");
            UnloadDelayQueue.ForceUnloadAll();
            Resources.UnloadUnusedAssets();
        }
    }
}
