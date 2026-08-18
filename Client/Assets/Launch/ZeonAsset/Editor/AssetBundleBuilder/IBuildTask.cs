namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 构建管线节点接口。
    /// </summary>
    public interface IBuildTask
    {
        string Name { get; }
        void Run(BuildContext context);
    }
}
