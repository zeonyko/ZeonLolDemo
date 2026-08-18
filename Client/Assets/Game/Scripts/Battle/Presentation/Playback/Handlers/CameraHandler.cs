using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>镜头轨：震动。和特效播放分开。</summary>
    public sealed class CameraHandler : IPlaybackHandler
    {
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            var session = BattleSystem.Instance?.Session;
            if (session == null) return;

            var cam = EntityManager.Instance?
                .GetEntity(BattleSystem.Instance.LocalPlayerId)?
                .GetComponent<CameraFollowComponent>();
            if (cam == null) return;

            string token = !string.IsNullOrEmpty(clip.Event.Param) ? clip.Event.Param : clip.Event.Key;
            if (string.IsNullOrEmpty(token))
                token = SkillTimelineKeys.HitStop;

            bool isShake = token.IndexOf("Shake", System.StringComparison.OrdinalIgnoreCase) >= 0
                || token == SkillTimelineKeys.HitStop;
            if (!isShake) return;

            float amp = token.IndexOf("Heavy", System.StringComparison.OrdinalIgnoreCase) >= 0
                ? 0.28f
                : 0.16f;
            float dur = clip.Event.Duration > 0.01f ? clip.Event.Duration : 0.12f;
            cam.PlayShake(dur, amp);
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }
        public void OnExit(PlaybackContext ctx, PlaybackClip clip) { }
    }
}
