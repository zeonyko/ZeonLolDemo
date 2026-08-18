using System;
using UnityEngine;

namespace Launch
{
    /// <summary>
    /// 反射进入热更入口。禁止在 Launch 里 using Login / Game 热更命名空间。
    /// </summary>
    public static class GameAppBootstrap
    {
        public const string AssemblyName = HybridClrDllPaths.EntryAssemblyName;
        public const string TypeName = "Login.LoginGameApp";

        public static bool TryStart()
        {
            var type =
                Type.GetType(TypeName + ", " + AssemblyName) ??
                Type.GetType(TypeName);
            if (type == null)
            {
                Debug.LogWarning(
                    "[Launch] 找不到 Login.LoginGameApp。请确认 Login.HotUpdate 已编译且含 Login.LoginGameApp。");
                return false;
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                Debug.LogError("[Launch] 创建 Login.LoginGameApp 失败: " + e.Message);
                return false;
            }

            if (!(instance is IGameApp app))
            {
                Debug.LogError("[Launch] Login.LoginGameApp 未实现 IGameApp。");
                return false;
            }

            app.StartApp();
            return true;
        }
    }
}
