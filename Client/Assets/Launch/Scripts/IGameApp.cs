namespace Launch
{
    /// <summary>
    /// 热更入口契约。Launch 不能引用热更程序集，只认此接口。
    /// 实现放在 Login.HotUpdate（Login.LoginGameApp），由 <see cref="GameAppBootstrap"/> 反射创建。
    /// </summary>
    public interface IGameApp
    {
        void StartApp();
    }
}
