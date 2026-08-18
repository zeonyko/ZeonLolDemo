namespace Game.ZeonAsset
{
    /// <summary>
    /// 引用计数接口。
    /// </summary>
    public interface IRefable
    {
        int RefCount { get; }
        void Retain();
        void Release();
    }
}
