using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗画面事件：挥砍、受击、死亡等。</summary>
    public enum ECombatViewEvent
    {
        AttackSwing,
        Hit,
        Death,
    }

    /// <summary>派发给 CombatViewComponent 的表现事件数据。</summary>
    public struct CombatViewEvent
    {
        public ECombatViewEvent Type;
        public Vector3 HitFrom; // 受击来源世界坐标，用于计算抖动方向与朝向甩头
    }
}
