using System.Text.Json;

namespace Server.Network
{
    /// <summary>把协议 JSON 读成对象。</summary>
    public static class NetJson
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true
        };

        public static T Parse<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }
}
