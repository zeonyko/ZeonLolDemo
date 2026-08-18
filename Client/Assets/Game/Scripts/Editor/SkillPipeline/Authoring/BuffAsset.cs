using System.Collections.Generic;
using Shared;
using UnityEngine;

namespace Client.SkillAuthoring
{
    /// <summary>Buff 编辑资产。</summary>
    [CreateAssetMenu(fileName = "Buff_New", menuName = "Battle/Buff Asset", order = 11)]
    public class BuffAsset : ScriptableObject
    {
        [Header("--- 1. 基础信息 ---")]
        public int BuffId = 3000;
        public string BuffName = "新 Buff";
        public BuffType Type = BuffType.Debuff;
        public float DefaultDuration = 1.5f;

        [Header("--- 2. 叠加规则 ---")]
        public BuffStackType StackType = BuffStackType.OverrideDuration;
        public int MaxStacks = 1;

        [Header("--- 3. 造成的状态 ---")]
        [Tooltip("挂上后给目标什么状态：眩晕/减速/禁锢/击飞…")]
        public int StatusFlags;

        [Header("--- 4. 属性修改 ---")]
        public List<BuffAttributeModifier> AttributeModifiers = new List<BuffAttributeModifier>();

        [Header("--- 5. 持续掉血 ---")]
        public float TickInterval;
        public float PeriodDamage;
        [Tooltip("周期伤害或状态。瞬时伤害不走 Buff。")]
        public BuffEffectKind EffectKind = BuffEffectKind.Status;
        [Tooltip("数值是配表写死，还是施法时填写。")]
        public EffectValueSource ValueSource = EffectValueSource.ConfigFlat;

        [Header("--- 6. 客户端表现 ---")]
        public string LoopVfxKey = "";
        public BuffAttachPoint AttachPoint = BuffAttachPoint.Foot;
        public Color TintColor = Color.white;
        [Range(0f, 1f)]
        public float TintStrength;
        [Range(0.05f, 2f)]
        public float AnimSpeedMultiplier = 1f;
        [Tooltip("循环特效缩放，1 为原尺寸")]
        public float LoopScale = 1f;
        [Tooltip("填了就会在挂上 Buff 时播这段表现（复杂控制用）")]
        public string PlaybackId = "";

        #region 数据转换：导出给运行时

        /// <summary>导出 Buff 逻辑配置（属性修改、周期效果等）。</summary>
        public BuffConfig ToData()
        {
            return new BuffConfig
            {
                BuffId = BuffId,
                Name = BuffName ?? "",
                BuffType = Type,
                DefaultDuration = DefaultDuration,
                BuffStackType = StackType,
                MaxStacks = MaxStacks > 0 ? MaxStacks : 1,
                StatusFlags = StatusFlags,
                AttributeModifiers = AttributeModifiers != null && AttributeModifiers.Count > 0
                    ? AttributeModifiers.ToArray()
                    : System.Array.Empty<BuffAttributeModifier>(),
                TickInterval = TickInterval,
                PeriodDamage = PeriodDamage,
                EffectKind = (int)EffectKind,
                ValueSource = (int)ValueSource
            };
        }

        /// <summary>导出 Buff 客户端表现配置（挂点、染色、动画倍速等）。</summary>
        public BuffPresentationConfig ToPresentation()
        {
            return new BuffPresentationConfig
            {
                BuffId = BuffId,
                LoopVfxKey = LoopVfxKey ?? "",
                Attach = AttachPoint,
                TintR = TintColor.r,
                TintG = TintColor.g,
                TintB = TintColor.b,
                TintA = TintColor.a,
                TintStrength = TintStrength,
                AnimSpeedMultiplier = AnimSpeedMultiplier > 0f ? AnimSpeedMultiplier : 1f,
                LoopScale = LoopScale > 0.01f ? LoopScale : 1f,
                PlaybackId = PlaybackId ?? ""
            };
        }

        #endregion

        #region 数据转换：从运行时数据反填

        /// <summary>从运行时配置反填编辑资产（逻辑字段 + 可选表现字段）。</summary>
        public void ApplyFromData(BuffConfig data, BuffPresentationConfig presentation = null)
        {
            if (data == null) return;
            BuffId = data.BuffId;
            BuffName = data.Name ?? "";
            Type = data.BuffType;
            DefaultDuration = data.DefaultDuration;
            StackType = data.BuffStackType;
            MaxStacks = data.MaxStacks > 0 ? data.MaxStacks : 1;
            StatusFlags = data.StatusFlags;
            AttributeModifiers = data.AttributeModifiers != null
                ? new List<BuffAttributeModifier>(data.AttributeModifiers)
                : new List<BuffAttributeModifier>();
            TickInterval = data.TickInterval;
            PeriodDamage = data.PeriodDamage;
            EffectKind = data.Kind != 0 ? data.Kind : BuffEffectKind.Status;
            ValueSource = data.ValueFrom;

            if (presentation != null)
            {
                LoopVfxKey = presentation.LoopVfxKey ?? "";
                AttachPoint = presentation.Attach;
                TintColor = new Color(presentation.TintR, presentation.TintG, presentation.TintB, presentation.TintA);
                TintStrength = presentation.TintStrength;
                AnimSpeedMultiplier = presentation.AnimSpeedMultiplier > 0f ? presentation.AnimSpeedMultiplier : 1f;
                LoopScale = presentation.LoopScale > 0.01f ? presentation.LoopScale : 1f;
                PlaybackId = presentation.PlaybackId ?? "";
            }
        }

        #endregion
    }
}
