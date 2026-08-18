namespace Shared
{
    /// <summary>配置 JSON 怎么读怎么写。两端可以用不同实现。</summary>
    public interface IConfigJsonCodec
    {
        /// <summary>把 JSON 转成配置对象。</summary>
        T FromJson<T>(string json) where T : class;

        /// <summary>把配置对象写成 JSON。</summary>
        string ToJson<T>(T obj) where T : class;
    }
}
