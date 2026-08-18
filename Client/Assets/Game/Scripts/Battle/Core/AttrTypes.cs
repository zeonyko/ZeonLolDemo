namespace Client.Battle
{
    /// <summary>战斗属性编号，给血条等 UI 用。</summary>
    public enum EAttrId
    {
        Hp = 1,
        MaxHp = 2,
    }

    /// <summary>一次属性变化（旧值→新值），给 UI 和表现听。</summary>
    public struct AttrChange
    {
        public EAttrId Id;
        public float OldValue;
        public float NewValue;
    }
}
