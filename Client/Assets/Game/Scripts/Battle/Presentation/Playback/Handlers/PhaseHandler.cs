using Shared;

namespace Client.Battle
{
    /// <summary>逻辑阶段轨：后摇开始时通知施法组件交还朝向，走位由 SkillState 切回移动态。</summary>
    public sealed class PhaseHandler : IPlaybackHandler
    {
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            if (ctx?.Kind != PlaybackKind.Cast) return;
            if (clip.Event.Key != SkillTimelineKeys.Recovery) return;
            ctx.SourceEntity?.GetComponent<SkillCastComponent>()?.EnterRecovery();
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }

        public void OnExit(PlaybackContext ctx, PlaybackClip clip) { }
    }
}
