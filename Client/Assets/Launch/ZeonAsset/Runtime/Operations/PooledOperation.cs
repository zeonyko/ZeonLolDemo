namespace Game.ZeonAsset
{
    /// <summary>
    /// 带类型池的 Operation。子类 Create() 调 <see cref="Rent"/>，结束由 OperationSystem 延迟还池。
    /// </summary>
    public abstract class PooledOperation<TSelf> : AsyncOperationBase
        where TSelf : PooledOperation<TSelf>, new()
    {
        private static readonly ObjectPool<TSelf> Pool =
            new ObjectPool<TSelf>(null, op => op.OnRecycle());

        private bool _inPool;

        protected static TSelf Rent()
        {
            var op = Pool.Get();
            op._inPool = false;
            return op;
        }

        internal sealed override bool TryReturnToPool()
        {
            if (_inPool)
                return false;

            _inPool = true;
            Pool.Release((TSelf)this);
            return true;
        }
    }

    /// <summary>带返回值的池化 Operation。</summary>
    public abstract class PooledOperation<TSelf, TResult> : AsyncOperationBase<TResult>
        where TSelf : PooledOperation<TSelf, TResult>, new()
    {
        private static readonly ObjectPool<TSelf> Pool =
            new ObjectPool<TSelf>(null, op => op.OnRecycle());

        private bool _inPool;

        protected static TSelf Rent()
        {
            var op = Pool.Get();
            op._inPool = false;
            return op;
        }

        internal sealed override bool TryReturnToPool()
        {
            if (_inPool)
                return false;

            _inPool = true;
            Pool.Release((TSelf)this);
            return true;
        }
    }
}
