using System;
using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>
    /// 落点范围圈：优先靠技能 Presentation 的地面圈特效；
    /// 仅当表现表没有圈时，才按 AreaSpawns 补一层 Vfx_SlowRing（不再画 LineRenderer 调试线）。
    /// </summary>
    public static class SkillAimAreaView
    {
        #region 常量与状态
        private struct ActiveRing
        {
            public int SkillId;
            public float ExpireAt;
            public TransientFx Fx;
        }

        private static readonly List<ActiveRing> Rings = new List<ActiveRing>(4);
        private static Transform _root;
        #endregion

        #region 起手生成
        /// <summary>技能起手时按 AreaSpawns 在落点生成范围圈（同技能旧圈先清）。</summary>
        public static void TrySpawnFromCast(int skillId, Vector3 targetPos)
        {
            if (!SkillCatalog.TryGet(skillId, out var def) || def == null) return;
            if (HasPresentationGroundRing(skillId)) return;
            if (!TryGetAreaSpawns(def, out var areas) || areas == null || areas.Count == 0) return;

            EnsureRoot();
            CancelSkill(skillId);

            for (int a = 0; a < areas.Count; a++)
            {
                var area = areas[a];
                if (area?.Shape == null) continue;
                float radius = area.Shape.SizeX > 0.05f ? area.Shape.SizeX : 2f;
                float duration = area.Duration > 0.05f ? area.Duration : 3f;
                SpawnRing(skillId, targetPos, radius, duration);
            }
        }

        /// <summary>表现表已配 Aim/World 地面圈时，不再叠一层兜底圈。</summary>
        static bool HasPresentationGroundRing(int skillId)
        {
            if (!SkillPresentationCatalog.TryGet(skillId, out var pres) || pres?.Clips == null)
                return false;

            for (int i = 0; i < pres.Clips.Length; i++)
            {
                var clip = pres.Clips[i];
                if (clip == null) continue;
                string raw = !string.IsNullOrEmpty(clip.Param) ? clip.Param : clip.Key;
                if (string.IsNullOrEmpty(raw)) continue;

                string[] parts = raw.Split(',');
                if (parts.Length == 0) continue;
                string key = VfxLibrary.NormalizeKey(parts[0].Trim());
                if (!IsGroundRingKey(key)) continue;

                string bone = parts.Length >= 2 ? parts[1].Trim() : "";
                if (string.IsNullOrEmpty(bone)
                    || string.Equals(bone, "Aim", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bone, "Target", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bone, "World", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(bone, "Foot", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool IsGroundRingKey(string key)
        {
            return key == "Vfx_SlowRing"
                   || key == "Vfx_ExplodeRing"
                   || key == "Vfx_ShieldRing"
                   || key == "Vfx_SectorFan"
                   || key == "Vfx_GroundBox";
        }

        static void SpawnRing(int skillId, Vector3 targetPos, float radius, float duration)
        {
            var vfx = VfxManager.Instance;
            if (vfx == null) return;

            Vector3 feet = targetPos;
            feet.y = GameConstants.GroundY + 0.03f;
            var fx = vfx.PlayByVfxKey(
                "Vfx_SlowRing",
                feet,
                Vector3.forward,
                radius,
                duration,
                Vector3.one * radius);
            if (fx == null) return;

            Rings.Add(new ActiveRing
            {
                SkillId = skillId,
                ExpireAt = Time.time + duration,
                Fx = fx
            });
        }
        #endregion

        #region 生命周期与清理
        /// <summary>技能取消/打断时清掉该技能的地面圈。</summary>
        public static void CancelSkill(int skillId)
        {
            if (skillId <= 0) return;
            for (int i = Rings.Count - 1; i >= 0; i--)
            {
                if (Rings[i].SkillId != skillId) continue;
                Rings[i].Fx?.Cancel();
                Rings.RemoveAt(i);
            }
        }

        /// <summary>每帧回收已过期的圈。</summary>
        public static void Tick()
        {
            float now = Time.time;
            for (int i = Rings.Count - 1; i >= 0; i--)
            {
                if (now < Rings[i].ExpireAt) continue;
                Rings[i].Fx?.Cancel();
                Rings.RemoveAt(i);
            }
        }

        /// <summary>立即销毁所有圈（战斗结束/根节点销毁时调用）。</summary>
        public static void Clear()
        {
            for (int i = 0; i < Rings.Count; i++)
                Rings[i].Fx?.Cancel();
            Rings.Clear();
        }

        /// <summary>退出战斗：清圈并拆掉 DontDestroyOnLoad 根节点。</summary>
        public static void Shutdown()
        {
            Clear();
            if (_root == null) return;
            UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }
        #endregion

        #region 配置解析与根节点管理
        /// <summary>从技能配置中提取有效的 AreaSpawn 落点区域列表。</summary>
        static bool TryGetAreaSpawns(SkillConfig def, out List<SkillAreaPayload> areas)
        {
            areas = null;
            if (def?.Clips == null) return false;
            for (int i = 0; i < def.Clips.Length; i++)
            {
                var hit = def.Clips[i]?.Hit;
                if (hit == null || !hit.HasAreaSpawns) continue;
                areas = new List<SkillAreaPayload>();
                for (int a = 0; a < hit.AreaSpawns.Length; a++)
                {
                    var area = hit.AreaSpawns[a];
                    if (area != null && area.HasContent)
                        areas.Add(area);
                }
                return areas.Count > 0;
            }
            return false;
        }

        /// <summary>第一次用时才创建瞄准圈根节点。</summary>
        static void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("SkillAimAreaViewRoot");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _root = go.transform;
            go.AddComponent<SkillAimAreaViewTicker>();
        }

        /// <summary>挂在根节点上，把 MonoBehaviour 生命周期转发给静态方法。</summary>
        private sealed class SkillAimAreaViewTicker : MonoBehaviour
        {
            private void LateUpdate() => Tick();
            private void OnDestroy() => Clear();
        }
        #endregion
    }
}
