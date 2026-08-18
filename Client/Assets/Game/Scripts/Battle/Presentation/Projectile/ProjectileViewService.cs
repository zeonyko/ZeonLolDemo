using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>弹道画面：飞行中往前推；命中后球体可留着当引信，晚点再炸。</summary>
    public sealed class ProjectileViewService
    {
        #region 全局一份与追踪
        public static ProjectileViewService Instance { get; private set; }

        private readonly Dictionary<long, ViewState> _views = new Dictionary<long, ViewState>(32);

        /// <summary>单条弹道在客户端的表现追踪数据（外推位置 + 关联的飞行特效）。</summary>
        private sealed class ViewState
        {
            public long InstanceId;
            public int ProjectileId;
            public int SkillId;
            public long TargetId;
            public float Speed;
            public float MaxDistance;
            public float Traveled;
            public Vector3 Pos;
            public Vector3 Dir;
            public TransientFx Fx;
        }
        #endregion

        #region 生命周期
        public static ProjectileViewService Create()
        {
            Instance = new ProjectileViewService();
            return Instance;
        }

        public void Shutdown()
        {
            Clear();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>取消所有飞行中特效并清空追踪表。</summary>
        public void Clear()
        {
            foreach (var kv in _views)
                kv.Value.Fx?.Cancel();
            _views.Clear();
        }
        #endregion

        #region 服务器包：生成 / 消失
        /// <summary>服务器生成弹道：播飞行特效并登记，每帧往前推位置。</summary>
        public void OnSpawn(S2C_Battle_ProjectileSpawnPacket msg)
        {
            if (msg == null || msg.ProjectileInstanceId == 0) return;

            if (_views.TryGetValue(msg.ProjectileInstanceId, out var existing))
            {
                existing.Fx?.Cancel();
                _views.Remove(msg.ProjectileInstanceId);
            }

            Vector3 origin = msg.Origin != null
                ? new Vector3(msg.Origin.X, msg.Origin.Y, msg.Origin.Z)
                : Vector3.zero;
            Vector3 dir = msg.Dir != null
                ? new Vector3(msg.Dir.X, 0f, msg.Dir.Z)
                : Vector3.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            float speed = msg.Speed > 0.1f ? msg.Speed : 16f;
            float maxDist = msg.MaxDistance > 0.1f ? msg.MaxDistance : GameConstants.DefaultCastRange;

            var fx = VfxManager.Instance?.PlayAuthProjectile(
                msg.ProjectileId, origin, dir, speed, maxDist);

            _views[msg.ProjectileInstanceId] = new ViewState
            {
                InstanceId = msg.ProjectileInstanceId,
                ProjectileId = msg.ProjectileId,
                SkillId = msg.SkillId,
                TargetId = msg.TargetId,
                Speed = speed,
                MaxDistance = maxDist,
                Traveled = 0f,
                Pos = fx != null ? fx.transform.position : origin,
                Dir = dir,
                Fx = fx
            };
        }

        /// <summary>服务器弹道消失：没命中就取消特效；命中了按引信延迟再炸。</summary>
        public void OnDespawn(S2C_Battle_ProjectileDespawnPacket msg)
        {
            if (msg == null) return;

            Vector3 fallbackPos = Vector3.zero;
            int skillId = msg.SkillId;
            int projectileId = 0;
            TransientFx flyingFx = null;
            if (_views.TryGetValue(msg.ProjectileInstanceId, out var view))
            {
                fallbackPos = view.Pos;
                projectileId = view.ProjectileId;
                if (skillId == 0) skillId = view.SkillId;
                flyingFx = view.Fx;
                // 先从追踪表摘掉，是否立刻销毁特效看 Impact
                view.Fx = null;
                _views.Remove(msg.ProjectileInstanceId);
            }

            Vector3 impactPos = fallbackPos;
            if (msg.ImpactPos != null)
                impactPos = new Vector3(msg.ImpactPos.X, msg.ImpactPos.Y, msg.ImpactPos.Z);

            if (projectileId == 0
                && skillId != 0
                && SkillCatalog.TryGet(skillId, out var def)
                && def != null)
                projectileId = def.ProjectileId;

            ProjectileCatalog.TryGet(projectileId, out var proj);

            if (msg.HasImpact == 0)
            {
                flyingFx?.Cancel();
                return;
            }

            float radius = VisualImpactRadius(proj);
            float delay = proj != null ? proj.ImpactDelay : 0f;

            if (delay > 0.05f)
            {
                // 命中后保留火球作引信，延迟再爆（不再立刻 Cancel）
                VfxManager.Instance?.PlayDelayedImpactFuse(impactPos, radius, delay, flyingFx);
                return;
            }

            flyingFx?.Cancel();
            VfxManager.Instance?.PlayExplodeRing(impactPos, radius);
        }

        #endregion

        #region 每帧外推
        /// <summary>按速度往前推所有弹道。锁定弹先转向目标；飞满射程或等服务器消失前先摘表。</summary>
        public void Tick(float dt)
        {
            if (_views.Count == 0 || dt <= 0f) return;

            List<long> remove = null;
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v.TargetId != 0)
                    RetargetLocked(v);

                float step = v.Speed * dt;
                float remain = v.MaxDistance - v.Traveled;
                if (remain <= 0.0001f)
                {
                    remove ??= new List<long>(4);
                    remove.Add(kv.Key);
                    continue;
                }

                if (step > remain) step = remain;
                v.Pos += v.Dir * step;
                v.Traveled += step;

                if (v.Fx != null)
                {
                    v.Fx.transform.position = v.Pos;
                    v.Fx.WorldVelocity = Vector3.zero;
                }

                if (v.Traveled >= v.MaxDistance - 0.0001f)
                {
                    remove ??= new List<long>(4);
                    remove.Add(kv.Key);
                }
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++)
            {
                long id = remove[i];
                if (!_views.TryGetValue(id, out var view)) continue;
                // 本地飞满射程：等服务器消失包决定炸不炸；这里只摘表，特效留给消失包接管
                // 若服务器包丢了，persist 安全网会在数秒内自毁
                if (view.Fx != null)
                {
                    view.Fx.Duration = 0.8f;
                    view.Fx.FadeRenderer = true;
                    view.Fx.Shrink = true;
                }
                _views.Remove(id);
            }
        }

        /// <summary>锁定目标的弹道每帧重新计算朝向目标的方向。</summary>
        private static void RetargetLocked(ViewState v)
        {
            var target = EntityManager.Instance?.GetEntity(v.TargetId);
            var tf = target?.GetComponent<TransformComponent>();
            if (tf == null) return;

            Vector3 to = tf.Position - v.Pos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;
            v.Dir = to.normalized;
        }
        #endregion

        #region 爆炸半径（画面）
        /// <summary>贴地环用地面区域半径；没有再退回爆炸判定半径，避免脉冲圈看起来比灼烧圈大一圈。</summary>
        static float VisualImpactRadius(ProjectileConfig proj)
        {
            float impact = proj != null ? proj.ImpactRadius : 0f;
            float area = 0f;
            if (proj?.GroundArea?.Shape != null)
                area = proj.GroundArea.Shape.SizeX;
            if (area > 0.1f && impact > 0.1f)
                return Mathf.Min(impact, area);
            if (area > 0.1f) return area;
            return impact > 0.1f ? impact : 1.5f;
        }
        #endregion
    }
}
