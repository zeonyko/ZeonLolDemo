using System;
using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    #region 轨道与事件数据
    /// <summary>表现轨 ID（与 SkillTrackId / 编辑器导出一致）。</summary>
    public enum PlaybackTrack
    {
        Anim = SkillTrackId.Anim,
        Audio = SkillTrackId.Audio,
        Vfx = SkillTrackId.Vfx,
        Camera = SkillTrackId.Camera,
        HitStop = SkillTrackId.HitStop,
        Phase = SkillTrackId.Phase,
        Motion = SkillTrackId.Motion,
        Window = SkillTrackId.WindowCancel,
        WindowAccept = SkillTrackId.WindowAccept,
    }

    /// <summary>播放上下文的用途：施法表现 or 受击反馈表现。</summary>
    public enum PlaybackKind
    {
        Cast,
        Reaction,
    }

    /// <summary>时间轴上一条区间事件（从技能配置预先整理好）。</summary>
    public struct PlaybackEvent
    {
        public float Time;      // 起始时间
        public float Duration;  // 区间时长
        public PlaybackTrack Track;
        public string Key;
        public string Param;

        public PlaybackEvent(float time, PlaybackTrack track, string key, string param = "", float duration = 0f)
        {
            Time = time;
            Duration = SkillTimelineKeys.ResolveClipDuration(duration);
            Track = track;
            Key = key ?? "";
            Param = param ?? "";
        }

        public float EndTime => Time + Duration;
    }

    /// <summary>当前激活的区间 Clip（Enter→Update→Exit）。</summary>
    public sealed class PlaybackClip
    {
        public PlaybackEvent Event;
        public float LocalTime;
        public object State; // 处理器自定义的运行时状态（如已生成的特效实例）
    }
    #endregion

    #region 处理器接口
    /// <summary>一条轨道怎么播，按轨道类型分发。</summary>
    public interface IPlaybackHandler
    {
        void OnEnter(PlaybackContext ctx, PlaybackClip clip);
        void OnUpdate(PlaybackContext ctx, PlaybackClip clip);
        void OnExit(PlaybackContext ctx, PlaybackClip clip);
    }
    #endregion

    #region 播放上下文
    /// <summary>播放器上下文：Cast / Reaction 共用，按 Kind 与字段取用。</summary>
    public sealed class PlaybackContext
    {
        // 通用
        public PlaybackKind Kind;
        public Entity SourceEntity;
        public Entity TargetEntity;
        public Vector3 WorldPos;
        public float DeltaTime;
        public float TimelineTime;

        // Cast 专用
        public int SkillId;
        public Vector3 CastDir;
        public Vector3 AimPos;
        public bool AllowLogicalMotion;
        public PlaybackLifetime Lifetime;

        // Reaction 专用
        public float Damage;
        public Vector3 KnockbackDir;
        /// <summary>表现用命中标志（暴击/闪避等），不参与逻辑结算。</summary>
        public uint HitFlags;
        /// <summary>表现用伤害类型（物/魔/真），由技能配表推断。</summary>
        public DamageType DamageType;
    }
    #endregion

    #region 播放请求与句柄
    /// <summary>一次播放请求。技能侧拼好，播放器只消费。</summary>
    public sealed class PlaybackData
    {
        public string Tag;
        public PlaybackEvent[] Events;
        public PlaybackContext Context;
    }

    /// <summary>播放会话的外部句柄；Id&lt;=0 视为无效句柄。</summary>
    public readonly struct PlaybackHandle
    {
        public readonly int Id;

        public PlaybackHandle(int id) => Id = id;

        public bool IsValid => Id > 0;

        public static readonly PlaybackHandle Invalid = default;
    }
    #endregion

    #region 施法生命周期回调
    /// <summary>Cast 生命周期：结束时还原 Anim；仅打断时取消 FX。</summary>
    public sealed class PlaybackLifetime
    {
        private readonly List<Action> _onComplete = new List<Action>(8); // 自然结束 + 打断都会执行
        private readonly List<Action> _onAbort = new List<Action>(8);    // 仅打断时执行

        public void Clear()
        {
            _onComplete.Clear();
            _onAbort.Clear();
        }

        /// <summary>施法自然结束与打断都会执行（如还原 Idle）。</summary>
        public void Register(Action undo)
        {
            if (undo != null)
                _onComplete.Add(undo);
        }

        /// <summary>仅打断/拒绝时执行（如立刻销毁弹道）；自然结束不调用，交给 TransientFx 自消。</summary>
        public void RegisterAbort(Action undo)
        {
            if (undo != null)
                _onAbort.Add(undo);
        }

        /// <summary>施法自然结束：只回放 onComplete 回调。</summary>
        public void Complete()
        {
            InvokeReverse(_onComplete);
            Clear();
        }

        /// <summary>施法被打断/拒绝：先回放 onAbort，再回放 onComplete。</summary>
        public void Abort()
        {
            InvokeReverse(_onAbort);
            InvokeReverse(_onComplete);
            Clear();
        }

        /// <summary>按注册的逆序依次调用（后注册先执行），随后清空列表。</summary>
        private static void InvokeReverse(List<Action> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                list[i]?.Invoke();
            list.Clear();
        }
    }
    #endregion
}
