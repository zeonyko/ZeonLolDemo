namespace Client.Battle
{
    /// <summary>一次正在播的施法：查接招/取消窗口，可打断。</summary>
    public sealed class SkillAbility
    {
        private readonly SkillPlaybackConfig _config;
        private readonly PlaybackHandle _handle;
        private readonly PlaybackLifetime _lifetime;

        public bool IsPlaying => Playback.Instance != null && Playback.Instance.IsPlaying(_handle);
        public int SkillId => _config != null ? _config.SkillId : 0;
        public float Time => Playback.Instance != null ? Playback.Instance.GetTime(_handle) : 0f;

        /// <summary>现在能不能直接接下一个技能（连招窗口）。</summary>
        public bool IsInAcceptWindow => IsInAnyWindow(_config?.AcceptWindows, Time);

        /// <summary>现在能不能被取消打断（如前摇闪避）。</summary>
        public bool IsInCancelWindow => IsInAnyWindow(_config?.CancelWindows, Time);

        internal SkillAbility(SkillPlaybackConfig config, PlaybackHandle handle, PlaybackLifetime lifetime)
        {
            _config = config;
            _handle = handle;
            _lifetime = lifetime;
        }

        public void Abort()
        {
            Playback.Instance?.Stop(_handle);
            _lifetime?.Abort();
        }

        /// <summary>时间轴自然播完：还原动作，飞行中的弹道特效留下。</summary>
        public void Finish()
        {
            Playback.Instance?.Stop(_handle);
            _lifetime?.Complete();
        }

        private static bool IsInAnyWindow(CastTimeWindow[] windows, float t)
        {
            if (windows == null || windows.Length == 0) return false;
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].Contains(t))
                    return true;
            }
            return false;
        }
    }
}
