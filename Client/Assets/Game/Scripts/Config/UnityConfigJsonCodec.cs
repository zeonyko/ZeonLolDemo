using Shared;
using UnityEngine;

namespace Client
{
    /// <summary>Unity 侧配置 JSON 读写，用 JsonUtility。</summary>
    public sealed class UnityConfigJsonCodec : IConfigJsonCodec
    {
        // 空输入返回 null，避免 JsonUtility 报错
        public T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonUtility.FromJson<T>(json);
        }

        // 带缩进，方便看
        public string ToJson<T>(T obj) where T : class
        {
            return obj == null ? "" : JsonUtility.ToJson(obj, true);
        }
    }
}
