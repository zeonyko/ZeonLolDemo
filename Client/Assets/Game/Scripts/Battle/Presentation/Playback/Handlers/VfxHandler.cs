using Shared;
using UnityEngine;

namespace Client.Battle
{
    public sealed class VfxHandler : IPlaybackHandler
    {
        #region 播放处理器
        /// <summary>参数：特效路径，可选挂点和偏移。击飞/状态飘字走反馈模块。</summary>
        public void OnEnter(PlaybackContext ctx, PlaybackClip clip)
        {
            if (ctx == null || clip == null) return;

            string key = clip.Event.Key ?? "";
            if (string.Equals(key, "Airborne", System.StringComparison.OrdinalIgnoreCase))
            {
                ApplyAirborne(ctx, clip.Event.Param);
                return;
            }
            if (string.Equals(key, "StatusPopup", System.StringComparison.OrdinalIgnoreCase))
            {
                ApplyStatusPopup(ctx, clip.Event.Param);
                return;
            }

            SpawnEntityFx(ctx, clip);
        }

        public void OnUpdate(PlaybackContext ctx, PlaybackClip clip) { }

        public void OnExit(PlaybackContext ctx, PlaybackClip clip)
        {
            // Vfx 轨默认是触发点（时长常为 0.05s），一次性特效交给 TransientFx 自己到期。
            // persist（弹道等）随 Clip 结束立刻收；打断走 Lifetime.RegisterAbort。
            if (clip?.State is TransientFx fx && fx.Persist)
                fx.Cancel();
            if (clip != null)
                clip.State = null;
        }
        #endregion

        #region 默认分支：挂点/世界特效生成
        /// <summary>解析 Param 生成挂点或世界特效，并登记 Abort 时的取消回调。</summary>
        private static void SpawnEntityFx(PlaybackContext ctx, PlaybackClip clip)
        {
            var actor = ctx.SourceEntity;
            if (actor == null) return;

            var fxComp = actor.GetComponent<FXComponent>();
            if (fxComp == null) return;

            string raw = !string.IsNullOrEmpty(clip.Event.Param) ? clip.Event.Param : clip.Event.Key;
            if (string.IsNullOrEmpty(raw)) return;

            ParseParam(raw, out string fxPath, out string bone, out Vector3 offset, out bool hasOffset);
            if (string.IsNullOrEmpty(fxPath)) return;
            if (ShouldSkipGroundFx(ctx, fxPath)) return;

            Vector3 forward = Vector3.zero;
            float range = 0f;
            Vector3? scaleMul = null;
            if (ctx.Kind == PlaybackKind.Cast)
            {
                forward = ctx.CastDir;
                var def = SkillCatalog.Get(ctx.SkillId);
                if (!TryResolveCastScale(def, clip.Event.Time, fxPath, out scaleMul))
                    range = def != null ? def.CastRange : 0f;
            }

            float duration = clip.Event.Duration;

            TransientFx spawned;
            if (string.Equals(bone, "World", System.StringComparison.OrdinalIgnoreCase))
            {
                Vector3 worldPos = hasOffset ? offset : ctx.WorldPos;
                spawned = fxComp.SpawnWorld(fxPath, worldPos, forward, duration, range, ctx, scaleMul);
            }
            else if (string.Equals(bone, "Aim", System.StringComparison.OrdinalIgnoreCase)
                     || string.Equals(bone, "Target", System.StringComparison.OrdinalIgnoreCase))
            {
                Vector3 aim = ctx.AimPos.sqrMagnitude > 0.0001f ? ctx.AimPos : ctx.WorldPos;
                if (hasOffset) aim += offset;
                spawned = fxComp.SpawnWorld(fxPath, aim, forward, duration, range, ctx, scaleMul);
            }
            else
            {
                spawned = fxComp.SpawnAttached(
                    fxPath, bone, hasOffset ? offset : Vector3.zero, duration, forward, range, ctx, scaleMul);
            }

            if (spawned == null) return;
            clip.State = spawned;
            ctx.Lifetime?.RegisterAbort(() => spawned.Cancel());
        }
        #endregion

        #region 特殊 Key 分支（Airborne / StatusPopup）
        /// <summary>Param: height,duration（秒）。</summary>
        static void ApplyAirborne(PlaybackContext ctx, string param)
        {
            var target = ctx.TargetEntity ?? ctx.SourceEntity;
            var combat = target?.GetComponent<CombatViewComponent>();
            if (combat == null) return;

            float height = 2.2f;
            float duration = 0.9f;
            if (!string.IsNullOrEmpty(param))
            {
                var parts = param.Split(',');
                if (parts.Length >= 1
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float h))
                    height = h;
                if (parts.Length >= 2
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float d))
                    duration = d;
            }
            combat.PlayAirborneBurst(height, duration);
        }

        /// <summary>Param: label,r,g,b,a</summary>
        static void ApplyStatusPopup(PlaybackContext ctx, string param)
        {
            if (string.IsNullOrEmpty(param) || VfxManager.Instance == null) return;

            var parts = param.Split(',');
            string label = parts[0].Trim();
            if (string.IsNullOrEmpty(label)) return;

            Color color = new Color(1f, 0.7f, 0.3f, 1f);
            if (parts.Length >= 5
                && float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r)
                && float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float g)
                && float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float b)
                && float.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float a))
            {
                color = new Color(r, g, b, a);
            }

            var target = ctx.TargetEntity ?? ctx.SourceEntity;
            Vector3 pos = target?.GetComponent<TransformComponent>()?.Position ?? ctx.WorldPos;
            VfxManager.Instance.PlayStatusPopup(pos + Vector3.up * 1.6f, label, color);
        }
        #endregion

        #region Param 解析
        /// <summary>闪现只留起终点位移特效，不铺地面判定盒。</summary>
        static bool ShouldSkipGroundFx(PlaybackContext ctx, string fxPath)
        {
            if (ctx == null || ctx.Kind != PlaybackKind.Cast) return false;
            var def = SkillCatalog.Get(ctx.SkillId);
            if (def == null || def.ResolveMode != ESkillResolveMode.Blink) return false;
            string key = VfxLibrary.NormalizeKey(fxPath);
            return key == "Vfx_GroundBox" || key == "Vfx_SectorFan";
        }

        /// <summary>施法特效缩放对齐 Logic 形状：盒=(宽,1,长)，圆/扇=半径。</summary>
        static bool TryResolveCastScale(SkillConfig def, float clipTime, string fxPath, out Vector3? scaleMul)
        {
            scaleMul = null;
            if (def == null) return false;

            string key = VfxLibrary.NormalizeKey(fxPath);
            bool isBox = key == "Vfx_GroundBox";
            bool isRadius = key == "Vfx_SectorFan" || key == "Vfx_ShieldRing"
                            || key == "Vfx_ExplodeRing" || key == "Vfx_SlowRing";
            if (!isBox && !isRadius) return false;

            if (SkillClipUtil.TryGetShapeForTime(def.Clips, clipTime, out var shape) && shape != null)
            {
                if (isBox)
                {
                    float len = shape.SizeX > 0.05f ? shape.SizeX : 1f;
                    float wid = shape.SizeY > 0.05f ? shape.SizeY : 1f;
                    scaleMul = new Vector3(wid, 1f, len);
                    return true;
                }

                float radius = shape.SizeX > 0.05f ? shape.SizeX : 1f;
                scaleMul = Vector3.one * radius;
                return true;
            }

            int projId = SkillClipUtil.ResolvePrimaryProjectileId(def.Clips);
            if (projId != 0
                && ProjectileCatalog.TryGet(projId, out var proj)
                && proj != null
                && proj.ImpactRadius > 0.1f)
            {
                scaleMul = Vector3.one * proj.ImpactRadius;
                return true;
            }

            return false;
        }

        /// <summary>解析 "fxPath,bone,ox,oy,oz" 形式的挂点特效参数。</summary>
        private static void ParseParam(string raw, out string fxPath, out string bone, out Vector3 offset, out bool hasOffset)
        {
            fxPath = "";
            bone = "";
            offset = Vector3.zero;
            hasOffset = false;

            string[] parts = raw.Split(',');
            if (parts.Length == 0) return;

            fxPath = parts[0].Trim();
            if (parts.Length >= 2)
                bone = parts[1].Trim();

            if (parts.Length >= 5
                && float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)
                && float.TryParse(parts[4], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                offset = new Vector3(x, y, z);
                hasOffset = true;
            }
        }
        #endregion
    }
}
