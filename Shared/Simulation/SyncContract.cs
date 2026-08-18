namespace Shared
{
    /// <summary>
    /// 怎么跟服务器对齐位置。两端用同一套移动模拟。
    /// 服务器说模拟完哪一帧，本地从那个位置把还没确认的输入再走一遍。
    /// 位移技还没对上那一帧前，先别拉位置。
    /// </summary>
    public static class SyncContract
    {
        /// <summary>这一帧按了什么。</summary>
        public static class InputFlags
        {
            public const uint Jump = 1u << 0;
            /// <summary>水平闪现/冲锋提交；方向在 MoveIntent，距离在 MotionDistance。</summary>
            public const uint MotionCommit = 1u << 1;
        }

        public const int MaxPendingInputs = 64;
    }
}
