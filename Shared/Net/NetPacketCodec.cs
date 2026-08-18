using System;
using System.Collections.Generic;
using System.Text;

namespace Shared
{
    /// <summary>网络包格式：长度 + 操作码 + JSON。长度不含最前面那 4 字节。</summary>
    public static class NetPacketCodec
    {
        public const int HeaderSize = 4;
        public const int OpCodeSize = 2;
        public const int MaxPacketBody = 1024 * 64;

        #region --- 编解码 ---

        /// <summary>把操作码和 JSON 打成一个网络包。</summary>
        public static byte[] Encode(EOpCode opCode, string jsonPayload)
        {
            byte[] body = Encoding.UTF8.GetBytes(jsonPayload ?? string.Empty);
            int packetLength = OpCodeSize + body.Length;
            byte[] buffer = new byte[HeaderSize + packetLength];

            WriteInt32LittleEndian(buffer, 0, packetLength);
            WriteUInt16LittleEndian(buffer, HeaderSize, (ushort)opCode);
            Buffer.BlockCopy(body, 0, buffer, HeaderSize + OpCodeSize, body.Length);
            return buffer;
        }

        /// <summary>从缓冲区拆出完整包。拆过的字节会删掉。</summary>
        public static List<(EOpCode opCode, string json)> TryDecodeMany(List<byte> buffer)
        {
            var packets = new List<(EOpCode, string)>();

            while (true)
            {
                if (buffer.Count < HeaderSize)
                    break;

                int packetLength = ReadInt32LittleEndian(buffer, 0);
                if (packetLength < OpCodeSize || packetLength > MaxPacketBody)
                {
                    buffer.Clear();
                    break;
                }

                int totalSize = HeaderSize + packetLength;
                if (buffer.Count < totalSize)
                    break;

                ushort opCodeValue = ReadUInt16LittleEndian(buffer, HeaderSize);
                int bodyLength = packetLength - OpCodeSize;
                string json = string.Empty;
                if (bodyLength > 0)
                {
                    byte[] body = new byte[bodyLength];
                    buffer.CopyTo(HeaderSize + OpCodeSize, body, 0, bodyLength);
                    json = Encoding.UTF8.GetString(body);
                }

                packets.Add(((EOpCode)opCodeValue, json));
                buffer.RemoveRange(0, totalSize);
            }

            return packets;
        }

        #endregion

        #region --- 读写字节 ---

        private static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt16LittleEndian(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
        }

        private static int ReadInt32LittleEndian(List<byte> buffer, int offset)
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }

        private static ushort ReadUInt16LittleEndian(List<byte> buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        #endregion
    }
}
