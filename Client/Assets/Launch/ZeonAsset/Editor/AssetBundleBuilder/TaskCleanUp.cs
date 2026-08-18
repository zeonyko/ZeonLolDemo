using System;
using System.IO;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 5. 清理 Unity 原生构建废料。
    /// 商业包体只保留：*.bundle / 自定义 PackageManifest(.json/.bytes)。
    /// 删除：*.manifest、*.meta、以及输出目录同名的原生 AssetBundleManifest 文件。
    /// </summary>
    public class TaskCleanUp : IBuildTask
    {
        public string Name => "TaskCleanUp";

        public void Run(BuildContext context)
        {
            if (string.IsNullOrEmpty(context.OutputPath) || !Directory.Exists(context.OutputPath))
            {
                context.LogWarning("输出目录不存在，跳过清理。");
                return;
            }

            int deletedManifest = 0;
            int deletedMeta = 0;
            int deletedUnityRoot = 0;

            foreach (var file in Directory.GetFiles(context.OutputPath, "*", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                var ext = Path.GetExtension(file);

                // 保留正式产物
                if (IsKeepFile(fileName))
                    continue;

                // Unity 每个 AB 旁路生成的文本清单 + 若输出在 Assets 下产生的 .meta
                if (ext.Equals(".manifest", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    deletedManifest++;
                    continue;
                }

                if (ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    deletedMeta++;
                    continue;
                }

                // Unity 输出目录同名的原生 AssetBundleManifest（无扩展名），例如 StandaloneWindows64
                if (string.IsNullOrEmpty(ext))
                {
                    File.Delete(file);
                    deletedUnityRoot++;
                }
            }

            context.UnityManifest = null;
            context.Log($"清理完成: .manifest={deletedManifest}, .meta={deletedMeta}, unityRootManifest={deletedUnityRoot}");
        }

        private static bool IsKeepFile(string fileName)
        {
            return fileName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                   || fileName.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
