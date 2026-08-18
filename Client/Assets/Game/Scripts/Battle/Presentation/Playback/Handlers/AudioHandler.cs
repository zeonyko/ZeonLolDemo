using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>音频轨：到点播一次短音效。循环蓄力等靠 clip 自身长度，不在运行时合成。</summary>
    public sealed class AudioHandler : IPlaybackHandler
    {
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            if (ctx == null || clip == null) return;

            string key = !string.IsNullOrEmpty(clip.Event.Param) ? clip.Event.Param : clip.Event.Key;
            if (string.IsNullOrEmpty(key) || key == SkillTimelineKeys.Play)
                return;

            Vector3 pos = ctx.WorldPos;
            if (pos.sqrMagnitude < 0.0001f)
            {
                var actor = ctx.SourceEntity;
                pos = actor?.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
            }

            SfxLibrary.Play(key, pos);
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }

        public void OnExit(PlaybackContext ctx, PlaybackClip clip) { }
    }
}
