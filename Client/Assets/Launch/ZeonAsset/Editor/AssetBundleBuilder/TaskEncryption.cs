using System;
using System.IO;
using Game.ZeonAsset;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 构建后加密 Bundle（在 Hash/CRC 计算之前）。
    /// Offset：文件头插入垃圾字节；Xor：整文件 XOR。
    /// </summary>
    public class TaskEncryption : IBuildTask
    {
        public string Name => "TaskEncryption";

        public void Run(BuildContext context)
        {
            var mode = context.Parameters.EncryptMode;
            if (mode == EBundleEncryptMode.None)
            {
                context.Log("EncryptMode=None，跳过加密。");
                return;
            }

            int offset = Math.Max(0, context.Parameters.EncryptOffset);
            byte xorKey = context.Parameters.EncryptXorKey;
            int encrypted = 0;

            foreach (var pair in context.BundleMap)
            {
                var info = pair.Value;
                var path = Path.Combine(context.OutputPath, info.BundleName + ".bundle");
                if (!File.Exists(path))
                    throw new BuildException($"加密前 Bundle 不存在: {path}");

                if (mode == EBundleEncryptMode.Offset)
                {
                    if (offset <= 0)
                        throw new BuildException("EncryptOffset 必须 > 0。");
                    PrependOffsetHeader(path, offset);
                    info.EncryptMode = EBundleEncryptMode.Offset;
                    info.LoadOffset = offset;
                }
                else if (mode == EBundleEncryptMode.Xor)
                {
                    XorFileInPlace(path, xorKey);
                    info.EncryptMode = EBundleEncryptMode.Xor;
                    info.LoadOffset = 0;
                }

                encrypted++;
            }

            context.Log($"加密完成: mode={mode}, bundles={encrypted}, offset={offset}, xorKey=0x{xorKey:X2}");
        }

        private static void PrependOffsetHeader(string path, int offset)
        {
            var original = File.ReadAllBytes(path);
            var header = new byte[offset];
            // 固定可识别头，便于排查
            var magic = System.Text.Encoding.ASCII.GetBytes("ZEONOFFS");
            Buffer.BlockCopy(magic, 0, header, 0, Math.Min(magic.Length, offset));
            var combined = new byte[offset + original.Length];
            Buffer.BlockCopy(header, 0, combined, 0, offset);
            Buffer.BlockCopy(original, 0, combined, offset, original.Length);
            File.WriteAllBytes(path, combined);
        }

        private static void XorFileInPlace(string path, byte key)
        {
            var bytes = File.ReadAllBytes(path);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)(bytes[i] ^ key);
            File.WriteAllBytes(path, bytes);
        }
    }
}
