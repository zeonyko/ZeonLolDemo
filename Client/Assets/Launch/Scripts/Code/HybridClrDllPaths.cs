using System;
using System.Collections.Generic;

namespace Launch
{
    /// <summary>HybridCLR DLL 打进 AB 后的 location。真机 LoadRawFile；编辑器跳过 Assembly.Load。</summary>
    public static class HybridClrDllPaths
    {
        /// <summary>反射入口所在程序集（Login.LoginGameApp）。</summary>
        public const string EntryAssemblyName = "Login.HotUpdate";

        public const string HotUpdateDllFolder = "Assets/GameDlls/HotUpdate";
        public const string AotDllFolder = "Assets/GameDlls/AOT";

        /// <summary>与 HybridCLR patchAOTAssemblyList 对齐（无 .dll 后缀）。</summary>
        public static readonly string[] DefaultAotMetadataAssemblies =
        {
            "mscorlib",
            "System",
            "System.Core",
            "UnityEngine.CoreModule",
            "Game.ZeonAsset",
            "Game.Launch",
        };

        /// <summary>全部热更程序集。入口 Login 先 Load；Game 在进 MainBattle 前必须已 Load。</summary>
        public static readonly string[] HotUpdateAssemblyNames =
        {
            "Login.HotUpdate",
            "Game.HotUpdate",
        };

        public static string BytesLocation(string folder, string assemblyName)
        {
            return folder.TrimEnd('/', '\\') + "/" + assemblyName + ".dll.bytes";
        }

        public static string[] DefaultHotUpdateLocations
        {
            get
            {
                var list = new string[HotUpdateAssemblyNames.Length];
                for (int i = 0; i < HotUpdateAssemblyNames.Length; i++)
                    list[i] = BytesLocation(HotUpdateDllFolder, HotUpdateAssemblyNames[i]);
                return list;
            }
        }

        public static string[] DefaultAotLocations()
        {
            var list = new string[DefaultAotMetadataAssemblies.Length];
            for (int i = 0; i < DefaultAotMetadataAssemblies.Length; i++)
                list[i] = BytesLocation(AotDllFolder, DefaultAotMetadataAssemblies[i]);
            return list;
        }

        public static string[] ResolveLocations(string[] configured, string[] fallback)
        {
            if (configured != null)
            {
                var filled = new List<string>(configured.Length);
                for (int i = 0; i < configured.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(configured[i]))
                        filled.Add(configured[i].Trim());
                }

                if (filled.Count > 0)
                    return filled.ToArray();
            }

            return fallback ?? Array.Empty<string>();
        }
    }
}
