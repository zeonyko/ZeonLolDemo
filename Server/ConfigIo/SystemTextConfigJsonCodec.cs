using System.Text.Json;
using Shared;

namespace Server
{
    /// <summary>读、写配置表 JSON。</summary>
    public sealed class SystemTextConfigJsonCodec : IConfigJsonCodec
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Options);
        }

        public string ToJson<T>(T obj) where T : class
        {
            return obj == null ? "" : JsonSerializer.Serialize(obj, Options);
        }
    }
}
