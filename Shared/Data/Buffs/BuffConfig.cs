using System;

namespace Shared
{
    /// <summary>Buff 改哪条属性、加多少。</summary>
    [Serializable]
    public class BuffAttributeModifier
    {
        public AttributeType Attribute = AttributeType.MoveSpeed;
        public ModifyType Operation = ModifyType.PercentAdd;
        public float Value;
    }

    /// <summary>Buff 逻辑表。状态编号进战斗前要转换。特效在表现表。</summary>
    [Serializable]
    public class BuffConfig
    {
        public int BuffId;
        public string Name = "";
        /// <summary>BuffType，JSON 存 int。</summary>
        public int Type;
        public float DefaultDuration = 1.5f;
        /// <summary>BuffStackType，JSON 存 int。</summary>
        public int StackType;
        public int MaxStacks = 1;
        /// <summary>配表状态编号。和运行时不是同一套。</summary>
        public int StatusFlags;
        public BuffAttributeModifier[] AttributeModifiers = Array.Empty<BuffAttributeModifier>();
        public float TickInterval;
        public float PeriodDamage;
        /// <summary>周期伤害或状态。瞬时伤害不走 Buff。</summary>
        public int EffectKind = (int)BuffEffectKind.Status;
        /// <summary>数值是配表写死，还是施法时填写。</summary>
        public int ValueSource;

        public BuffEffectKind Kind
        {
            get => (BuffEffectKind)EffectKind;
            set => EffectKind = (int)value;
        }

        public EffectValueSource ValueFrom
        {
            get => (EffectValueSource)ValueSource;
            set => ValueSource = (int)value;
        }

        public BuffType BuffType
        {
            get => (BuffType)Type;
            set => Type = (int)value;
        }

        public BuffStackType BuffStackType
        {
            get => (BuffStackType)StackType;
            set => StackType = (int)value;
        }
    }

    /// <summary>运行时 Buff 表。</summary>
    public static class BuffCatalog
    {
        static readonly IdCatalog<BuffConfig> Items = new IdCatalog<BuffConfig>(
            c => c.BuffId,
            Prepare);

        static void Prepare(BuffConfig data)
        {
            if (data.MaxStacks < 1) data.MaxStacks = 1;
            if (data.AttributeModifiers == null)
                data.AttributeModifiers = Array.Empty<BuffAttributeModifier>();
        }

        public static void RegisterOrReplace(BuffConfig data) => Items.RegisterOrReplace(data);
        public static void ClearAndLoad(System.Collections.Generic.IEnumerable<BuffConfig> list) =>
            Items.ClearAndLoad(list);
        public static BuffConfig Get(int buffId) => Items.Get(buffId);
        public static bool TryGet(int buffId, out BuffConfig data) => Items.TryGet(buffId, out data);
        public static System.Collections.Generic.IReadOnlyDictionary<int, BuffConfig> All => Items.All;

    }
}
