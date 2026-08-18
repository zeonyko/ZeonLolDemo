using System.Collections.Generic;
using System.Globalization;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>Buff 外观：头顶持续状态字、身体染色、动画快慢、循环罩子特效。</summary>
    public class StatusFxComponent : Component
    {
        #region 依赖组件
        private ViewComponent _viewComp;
        private HeadUIComponent _headUi;
        private BuffComponent _buffComp;
        private AnimationComponent _animComp;
        private CombatViewComponent _combatView;

        private ViewComponent ViewComp => _viewComp ??= Owner?.GetComponent<ViewComponent>();
        private HeadUIComponent HeadUi => _headUi ??= Owner?.GetComponent<HeadUIComponent>();
        private BuffComponent BuffComp => _buffComp ??= Owner?.GetComponent<BuffComponent>();
        private AnimationComponent AnimComp => _animComp ??= Owner?.GetComponent<AnimationComponent>();
        private CombatViewComponent CombatView =>
            _combatView ??= Owner?.GetComponent<CombatViewComponent>();
        #endregion

        #region 状态
        private readonly HashSet<int> _playbackStarted = new HashSet<int>();
        private readonly HashSet<int> _sfxPlayed = new HashSet<int>();
        private readonly Dictionary<int, TransientFx> _loopFxByBuff = new Dictionary<int, TransientFx>(4);
        private readonly List<int> _statusBuffIds = new List<int>(4);
        private readonly List<(string Text, Color Color)> _labelScratch = new List<(string, Color)>(4);
        private const float LabelRefreshInterval = 0.05f;
        private float _labelRefreshAcc;
        #endregion

        #region 生命周期
        public override void OnDestroy()
        {
            ClearLoopFx();
            _playbackStarted.Clear();
            _sfxPlayed.Clear();
            _statusBuffIds.Clear();
            HeadUi?.SetStatusLabels(null);
            ViewComp?.ClearBodyTint();
            AnimComp?.SetBuffSpeedMultiplier(1f);
            CombatView?.SetAirborneVisual(false);
        }

        public override void OnUpdate(float dt)
        {
            RefreshLoopFxLifetime();

            if (_statusBuffIds.Count == 0) return;
            _labelRefreshAcc += dt;
            if (_labelRefreshAcc < LabelRefreshInterval) return;
            _labelRefreshAcc = 0f;
            RefreshStatusLabels();
        }
        #endregion

        #region 按服务器 Buff 刷新表现
        /// <summary>Buff 当前状态变了之后调用。</summary>
        public void SyncFromBuffs(IReadOnlyList<int> activeBuffIds)
        {
            activeBuffIds ??= System.Array.Empty<int>();
            var want = BuildWantSet(activeBuffIds);
            PruneStartedSets(want);
            SyncLoopFx(want);

            _statusBuffIds.Clear();
            Color tint = Color.white;
            float tintStr = 0f;
            float animMul = 1f;
            bool airborne = false;

            foreach (var id in want)
            {
                if (!BuffCatalog.TryGet(id, out var cfg) || cfg == null) continue;
                BuffPresentationCatalog.TryGet(id, out var pres);

                if (ShouldShowStatusLabel(cfg, pres))
                {
                    _statusBuffIds.Add(id);
                    if (_sfxPlayed.Add(id))
                    {
                        Vector3 pos = ViewComp != null
                            ? ViewComp.GetHeadAnchorWorldPos(0f)
                            : Vector3.zero;
                        SfxLibrary.Play("Sfx_BuffOn", pos);
                    }
                }

                if (!string.IsNullOrEmpty(pres?.PlaybackId) && _playbackStarted.Add(id))
                    TryPlayBuffPlayback(pres.PlaybackId);

                if (pres != null && pres.TintStrength > tintStr)
                {
                    tintStr = pres.TintStrength;
                    tint = new Color(pres.TintR, pres.TintG, pres.TintB, pres.TintA);
                }

                if (pres != null && pres.AnimSpeedMultiplier > 0f && pres.AnimSpeedMultiplier < animMul)
                    animMul = pres.AnimSpeedMultiplier;

                if ((cfg.StatusFlags & SkillEffectFlags.Airborne) != 0)
                    airborne = true;
            }

            _labelRefreshAcc = 0f;
            RefreshStatusLabels();

            if (tintStr > 0.01f)
                ViewComp?.SetBodyTint(Color.Lerp(Color.white, tint, tintStr));
            else
                ViewComp?.ClearBodyTint();

            AnimComp?.SetBuffSpeedMultiplier(animMul);
            CombatView?.SetAirborneVisual(airborne);
        }

        void RefreshStatusLabels()
        {
            _labelScratch.Clear();
            for (int i = 0; i < _statusBuffIds.Count; i++)
            {
                int id = _statusBuffIds[i];
                if (!BuffCatalog.TryGet(id, out var cfg) || cfg == null) continue;
                BuffPresentationCatalog.TryGet(id, out var pres);
                if (!ShouldShowStatusLabel(cfg, pres)) continue;

                Color c = Color.white;
                if (pres != null)
                    c = new Color(pres.TintR, pres.TintG, pres.TintB, 1f);

                float remain = BuffComp != null ? BuffComp.GetRemaining(id) : 0f;
                _labelScratch.Add((FormatStatusText(cfg.Name, remain), c));
            }

            HeadUi?.SetStatusLabels(_labelScratch.Count > 0 ? _labelScratch : null);
        }

        private static HashSet<int> BuildWantSet(IReadOnlyList<int> activeBuffIds)
        {
            var want = new HashSet<int>();
            for (int i = 0; i < activeBuffIds.Count; i++)
            {
                if (activeBuffIds[i] != 0)
                    want.Add(activeBuffIds[i]);
            }
            return want;
        }

        void PruneStartedSets(HashSet<int> want)
        {
            PruneSet(_playbackStarted, want);
            PruneSet(_sfxPlayed, want);
        }

        static void PruneSet(HashSet<int> set, HashSet<int> want)
        {
            if (set.Count == 0) return;
            var stale = new List<int>();
            foreach (var id in set)
            {
                if (!want.Contains(id))
                    stale.Add(id);
            }
            for (int i = 0; i < stale.Count; i++)
                set.Remove(stale[i]);
        }

        /// <summary>有名字且带状态标记或染色的 Buff 才显示持续字。</summary>
        static bool ShouldShowStatusLabel(BuffConfig cfg, BuffPresentationConfig pres)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.Name)) return false;
            bool hasStatus = cfg.StatusFlags != 0;
            bool hasTint = pres != null && pres.TintStrength > 0.01f;
            return hasStatus || hasTint;
        }

        static string FormatStatusText(string name, float remainSeconds)
        {
            if (remainSeconds < 0f) remainSeconds = 0f;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1:0.0}",
                name,
                remainSeconds);
        }

        void TryPlayBuffPlayback(string playbackId)
        {
            if (string.IsNullOrEmpty(playbackId) || Playback.Instance == null || Owner == null)
                return;
            var cfg = SkillReactionConfigLoader.Get(playbackId);
            if (cfg == null) return;
            Vector3 worldPos = ViewComp != null
                ? ViewComp.GetHeadAnchorWorldPos(0f)
                : Vector3.zero;
            Playback.Instance.Play(new PlaybackData
            {
                Tag = $"buff:{playbackId}",
                Events = cfg.Events,
                Context = new PlaybackContext
                {
                    Kind = PlaybackKind.Reaction,
                    SourceEntity = Owner,
                    TargetEntity = Owner,
                    WorldPos = worldPos
                }
            });
        }
        #endregion

        #region Buff 循环特效（罩子等）
        void SyncLoopFx(HashSet<int> want)
        {
            if (_loopFxByBuff.Count > 0)
            {
                var stale = new List<int>();
                foreach (var kv in _loopFxByBuff)
                {
                    if (!want.Contains(kv.Key))
                        stale.Add(kv.Key);
                }
                for (int i = 0; i < stale.Count; i++)
                    StopLoopFx(stale[i]);
            }

            foreach (var id in want)
            {
                if (_loopFxByBuff.ContainsKey(id)) continue;
                if (!BuffPresentationCatalog.TryGet(id, out var pres) || pres == null) continue;
                if (string.IsNullOrEmpty(pres.LoopVfxKey)) continue;
                StartLoopFx(id, pres);
            }
        }

        void StartLoopFx(int buffId, BuffPresentationConfig pres)
        {
            var view = ViewComp;
            if (view?.ViewGameObject == null) return;

            Transform root = view.ViewGameObject.transform;
            Vector3 offset = ResolveLoopOffset(view, pres.Attach);
            Vector3 forward = root.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            float scale = pres.LoopScale > 0.01f ? pres.LoopScale : 1f;
            var fx = VfxLibrary.SpawnTransient(
                pres.LoopVfxKey,
                root.position + offset,
                forward,
                persist: true,
                scaleMul: Vector3.one * scale);
            if (fx == null) return;

            fx.Follow(root, offset);
            _loopFxByBuff[buffId] = fx;
        }

        static Vector3 ResolveLoopOffset(ViewComponent view, BuffAttachPoint attach)
        {
            float pivot = view != null ? view.PivotOffsetY : 1f;
            switch (attach)
            {
                case BuffAttachPoint.Foot:
                    return Vector3.zero;
                case BuffAttachPoint.Head:
                    return Vector3.up * Mathf.Max(1.4f, pivot * 1.05f);
                case BuffAttachPoint.Chest:
                    return Vector3.up * Mathf.Max(0.55f, pivot * 0.55f);
                default:
                    return Vector3.up * Mathf.Max(0.4f, pivot * 0.35f);
            }
        }

        void StopLoopFx(int buffId)
        {
            if (!_loopFxByBuff.TryGetValue(buffId, out var fx)) return;
            _loopFxByBuff.Remove(buffId);
            fx?.Cancel();
        }

        void ClearLoopFx()
        {
            if (_loopFxByBuff.Count == 0) return;
            foreach (var kv in _loopFxByBuff)
                kv.Value?.Cancel();
            _loopFxByBuff.Clear();
        }

        void RefreshLoopFxLifetime()
        {
            if (_loopFxByBuff.Count == 0) return;
            foreach (var kv in _loopFxByBuff)
            {
                var fx = kv.Value;
                if (fx == null) continue;
                fx.RefreshLifetime();
            }
        }
        #endregion
    }
}
