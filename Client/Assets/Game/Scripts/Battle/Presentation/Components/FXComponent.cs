using System;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>实体特效：找挂点，按 Prefab key 播放。</summary>
    public class FXComponent : Component
    {
        #region 依赖组件
        private ViewComponent _viewComp;
        private ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        #endregion

        #region 公开 API：按挂点 / 世界坐标播放特效
        public TransientFx SpawnAttached(
            string fxPath,
            string attachBone = "",
            Vector3 localOffset = default,
            float duration = 0f,
            Vector3 forward = default,
            float range = 0f,
            PlaybackContext ctx = null,
            Vector3? scaleMul = null)
        {
            if (string.IsNullOrEmpty(fxPath)) return null;

            var viewComp = ViewComp;
            if (viewComp?.ViewGameObject == null) return null;

            ResolveAttach(viewComp, attachBone, localOffset, out Vector3 worldPos, out Vector3 boneForward);
            if (forward.sqrMagnitude > 0.0001f)
                boneForward = forward;

            return SpawnByPath(fxPath, worldPos, boneForward, duration, range, ctx, scaleMul);
        }

        public TransientFx SpawnWorld(
            string fxPath,
            Vector3 worldPos,
            Vector3 forward = default,
            float duration = 0f,
            float range = 0f,
            PlaybackContext ctx = null,
            Vector3? scaleMul = null)
        {
            if (string.IsNullOrEmpty(fxPath)) return null;

            if (forward.sqrMagnitude < 0.0001f)
            {
                var viewComp = ViewComp;
                if (viewComp?.ViewGameObject != null)
                {
                    forward = viewComp.ViewGameObject.transform.forward;
                    forward.y = 0f;
                }
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.forward;
                forward.Normalize();
            }

            return SpawnByPath(fxPath, worldPos, forward, duration, range, ctx, scaleMul);
        }
        #endregion

        #region 内部：按 Key 派发播放 / 挂点坐标解析
        private static TransientFx SpawnByPath(
            string fxPath, Vector3 worldPos, Vector3 forward, float duration, float range, PlaybackContext ctx,
            Vector3? scaleMul = null)
        {
            var vfx = VfxManager.Instance;
            if (vfx == null) return null;

            string key = VfxLibrary.NormalizeKey(fxPath);

            if (string.Equals(key, "Vfx_DamageFloat", StringComparison.OrdinalIgnoreCase))
            {
                float damage = ctx != null ? ctx.Damage : 0f;
                uint flags = ctx != null ? ctx.HitFlags : 0u;
                bool isDodge = (flags & (uint)Shared.EBattle_HitFlags.Dodge) != 0;
                // 伤害/治疗均显示；0 伤害仅闪避出 MISS
                if (Mathf.Abs(damage) < 0.01f && !isDodge)
                    return null;
                var dmgType = ctx != null ? ctx.DamageType : Shared.DamageType.Physical;
                vfx.PlayDamageFloat(worldPos, damage, flags, dmgType);
                return null;
            }

            return vfx.PlayByVfxKey(key, worldPos, forward, range, duration, scaleMul);
        }

        /// <summary>按挂点名解析出对应世界坐标与朝向：Root/Body 用根节点，Foot/Hand/Weapon/Head 用估算偏移，其余尝试按名字查找子节点。</summary>
        private static void ResolveAttach(
            ViewComponent view,
            string attachBone,
            Vector3 offsetPos,
            out Vector3 worldPos,
            out Vector3 forward)
        {
            Transform root = view.ViewGameObject.transform;
            forward = root.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            string bone = attachBone ?? "";
            Vector3 local = offsetPos;

            if (string.IsNullOrEmpty(bone) || bone == "Root" || bone == "Body")
            {
                worldPos = root.position + root.TransformDirection(local);
                return;
            }

            if (bone == "Foot" || bone == "Foot_R" || bone == "Foot_L" || bone == "Feet")
            {
                var feet = OwnerFeet(view);
                worldPos = feet + root.TransformDirection(local);
                return;
            }

            if (bone == "Hand" || bone == "Hand_R" || bone == "Hand_L")
            {
                worldPos = root.position + Vector3.up * (view.PivotOffsetY * 0.6f)
                           + forward * 0.45f
                           + root.TransformDirection(local);
                return;
            }

            if (bone == "Weapon" || bone == "Sword")
            {
                worldPos = root.position + Vector3.up * (view.PivotOffsetY * 0.55f)
                           + forward * 0.7f
                           + root.TransformDirection(local);
                return;
            }

            if (bone == "Head")
            {
                worldPos = view.GetHeadAnchorWorldPos(0f) + root.TransformDirection(local);
                return;
            }

            var child = root.Find(bone);
            if (child != null)
            {
                worldPos = child.TransformPoint(local);
                forward = child.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f) forward.Normalize();
                return;
            }

            worldPos = root.position + root.TransformDirection(local);
        }

        private static Vector3 OwnerFeet(ViewComponent viewComp)
        {
            var transformComp = viewComp.Owner?.GetComponent<TransformComponent>();
            return transformComp != null
                ? transformComp.Position
                : viewComp.ViewGameObject.transform.position - Vector3.up * viewComp.PivotOffsetY;
        }
        #endregion
    }
}
