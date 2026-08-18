namespace Shared
{
    /// <summary>伤害类型。JSON 存 int。</summary>
    public enum DamageType
    {
        Physical = 0,
        Magical = 1,
        True = 2
    }

    /// <summary>Buff 伤害数值从哪来：配表写死，或施法时临时填写。</summary>
    public enum EffectValueSource
    {
        ConfigFlat = 0,
        SetByCaller = 1
    }
}
