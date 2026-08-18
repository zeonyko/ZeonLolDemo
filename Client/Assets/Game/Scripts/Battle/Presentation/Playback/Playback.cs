using System.Collections.Generic;
using Shared;

namespace Client.Battle
{
    /// <summary>技能表现播放器：到点开始、每帧推进、结束。</summary>
    public sealed class Playback
    {
        #region 全局一份与当前状态
        public static Playback Instance { get; private set; }

        private readonly List<Session> _sessions = new List<Session>(16); // 当前所有进行中的播放会话
        private readonly AnimHandler _anim = new AnimHandler();       // Anim 轨处理器
        private readonly AudioHandler _audio = new AudioHandler();    // Audio 轨处理器
        private readonly VfxHandler _vfx = new VfxHandler();          // Vfx 轨处理器
        private readonly CameraHandler _camera = new CameraHandler(); // Camera / HitStop 轨处理器
        private readonly MotionHandler _motion = new MotionHandler(); // Motion 轨处理器
        private readonly PhaseHandler _phase = new PhaseHandler();   // Phase 轨：后摇交还
        private int _nextId = 1; // 会话句柄自增 ID，溢出后回绕到 1

        public static Playback Create()
        {
            Instance = new Playback();
            return Instance;
        }

        public void Shutdown()
        {
            Clear();
            if (Instance == this)
                Instance = null;
        }
        #endregion

        #region 播放入口
        /// <summary>开始播放；返回句柄供 SkillAbility 等查询/中止。</summary>
        public PlaybackHandle Play(PlaybackData data)
        {
            if (data?.Context == null || data.Events == null || data.Events.Length == 0)
                return PlaybackHandle.Invalid;

            int id = _nextId++;
            if (_nextId <= 0)
                _nextId = 1;

            var events = data.Events;
            float endTime = 0f;
            for (int i = 0; i < events.Length; i++)
            {
                // 接招/取消窗单独查，不拉长这次施法
                if (events[i].Track == PlaybackTrack.Window
                    || events[i].Track == PlaybackTrack.WindowAccept)
                    continue;
                float clipEnd = events[i].EndTime;
                if (clipEnd > endTime)
                    endTime = clipEnd;
            }

            var session = new Session
            {
                Id = id,
                Tag = data.Tag ?? "",
                Context = data.Context,
                Events = events,
                NextIndex = 0,
                Time = 0f,
                EndTime = endTime,
                Done = false,
                Active = new List<ActiveSlot>(8)
            };
            _sessions.Add(session);

            // 时间为 0 的片段马上开始，不等下一帧
            EnterDue(session);
            return new PlaybackHandle(id);
        }
        #endregion

        #region 帧驱动
        /// <summary>驱动所有会话前进一帧；已结束的会话从列表移除。</summary>
        public void Tick(float dt)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                var session = _sessions[i];
                if (session.Done)
                {
                    _sessions.RemoveAt(i);
                    continue;
                }

                TickSession(session, dt);
                if (session.Done)
                    _sessions.RemoveAt(i);
            }
        }
        #endregion

        #region 状态查询
        /// <summary>句柄对应的会话是否仍在播放中。</summary>
        public bool IsPlaying(PlaybackHandle handle)
        {
            if (!handle.IsValid) return false;
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Id == handle.Id)
                    return !_sessions[i].Done;
            }
            return false;
        }

        /// <summary>句柄对应会话已播放的时长；无效或找不到则返回 0。</summary>
        public float GetTime(PlaybackHandle handle)
        {
            if (!handle.IsValid) return 0f;
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Id == handle.Id)
                    return _sessions[i].Time;
            }
            return 0f;
        }
        #endregion

        #region 停止与清理
        /// <summary>中止指定会话：立即 Exit 所有活跃 Clip 并标记结束。</summary>
        public void Stop(PlaybackHandle handle)
        {
            if (!handle.IsValid) return;
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Id != handle.Id) continue;
                ExitAll(_sessions[i]);
                _sessions[i].Done = true;
                _sessions[i].NextIndex = _sessions[i].Events.Length;
                return;
            }
        }

        /// <summary>中止全部会话（如战斗结束/场景切换）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _sessions.Count; i++)
            {
                ExitAll(_sessions[i]);
                _sessions[i].Done = true;
                _sessions[i].NextIndex = _sessions[i].Events.Length;
            }
            _sessions.Clear();
        }
        #endregion

        #region 会话内部驱动
        /// <summary>推进单个会话的时间轴：先更新活跃 Clip，再检查新到期的 Clip。</summary>
        private void TickSession(Session session, float dt)
        {
            session.Time += dt;
            var ctx = session.Context;
            ctx.DeltaTime = dt;
            ctx.TimelineTime = session.Time;

            UpdateActive(session);
            EnterDue(session);

            if (session.Time >= session.EndTime && session.Active.Count == 0)
            {
                session.Done = true;
            }
        }

        /// <summary>把时间已到达的事件逐个 Enter，加入活跃列表。</summary>
        private void EnterDue(Session session)
        {
            var events = session.Events;
            var ctx = session.Context;
            while (session.NextIndex < events.Length && events[session.NextIndex].Time <= session.Time)
            {
                var e = events[session.NextIndex];
                session.NextIndex++;

                var handler = Resolve(e.Track);
                if (handler == null)
                    continue;

                var clip = new PlaybackClip
                {
                    Event = e,
                    LocalTime = session.Time - e.Time,
                    State = null
                };
                handler.OnEnter(ctx, clip);
                session.Active.Add(new ActiveSlot { Clip = clip, Handler = handler });
            }
        }

        /// <summary>更新活跃 Clip；到达 EndTime 的 Clip 触发 Exit 并移出列表。</summary>
        private void UpdateActive(Session session)
        {
            var ctx = session.Context;
            var active = session.Active;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var slot = active[i];
                var clip = slot.Clip;
                clip.LocalTime = session.Time - clip.Event.Time;

                if (session.Time >= clip.Event.EndTime)
                {
                    slot.Handler.OnExit(ctx, clip);
                    active.RemoveAt(i);
                    continue;
                }

                slot.Handler.OnUpdate(ctx, clip);
            }
        }

        /// <summary>立即 Exit 会话内所有活跃 Clip 并清空活跃列表。</summary>
        private void ExitAll(Session session)
        {
            if (session?.Active == null) return;
            var ctx = session.Context;
            for (int i = session.Active.Count - 1; i >= 0; i--)
            {
                var slot = session.Active[i];
                slot.Handler.OnExit(ctx, slot.Clip);
            }
            session.Active.Clear();
        }
        #endregion

        #region 轨道处理器分发
        /// <summary>按轨道类型取对应的处理器；HitStop 与 Camera 共用同一处理器。</summary>
        private IPlaybackHandler Resolve(PlaybackTrack track)
        {
            return track switch
            {
                PlaybackTrack.Anim => _anim,
                PlaybackTrack.Audio => _audio,
                PlaybackTrack.Vfx => _vfx,
                PlaybackTrack.Camera => _camera,
                PlaybackTrack.HitStop => _camera,
                PlaybackTrack.Motion => _motion,
                PlaybackTrack.Phase => _phase,
                _ => null
            };
        }
        #endregion

        #region 内部数据结构
        /// <summary>一个正在播放的 Clip 及其对应的轨道处理器。</summary>
        private sealed class ActiveSlot
        {
            public PlaybackClip Clip;
            public IPlaybackHandler Handler;
        }

        /// <summary>一次 Play() 请求对应的播放会话。</summary>
        private sealed class Session
        {
            public int Id;
            public string Tag;
            public PlaybackContext Context;
            public PlaybackEvent[] Events;
            public int NextIndex;   // 下一个待 Enter 的事件下标
            public float Time;      // 会话已播放时长
            public float EndTime;   // 最晚一个非 Window 轨 Clip 的结束时间
            public bool Done;
            public List<ActiveSlot> Active; // 当前处于 Enter~Exit 区间内的 Clip
        }
        #endregion
    }
}
