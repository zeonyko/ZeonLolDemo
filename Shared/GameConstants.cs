namespace Shared
{
    /// <summary>两端必须一致的数字：帧率、移速、技能/弹道/Buff 编号。别在两端各写一份。</summary>
    public static class GameConstants
    {
        #region --- 网络心跳 ---

        /// <summary>服务器每秒跑几帧。</summary>
        public const float ServerTickRate = 20f;
        /// <summary>每帧多长（秒）。</summary>
        public const float ServerTickDt = 1f / ServerTickRate;
        /// <summary>走路输入按这个间隔打包发出；本地仍每帧记账，不丢帧、不补帧。</summary>
        public const float InputSendInterval = ServerTickDt;
        /// <summary>一帧最多算这么多秒，卡住后不会瞬移。</summary>
        public const float MaxInputDeltaTime = 0.1f;
        /// <summary>卡住后再恢复，一次最多补算这么多秒，避免瞬移。</summary>
        public const float MaxInputSimBudget = 1.0f;
        /// <summary>本地先放技能后，等多久等服务器确认（秒）。</summary>
        public const float CastAckTimeoutSeconds = 1.5f;

        #endregion

        #region --- 移动 ---

        /// <summary>英雄走路速度。要比小兵快，但不能快到离谱。</summary>
        public const float MoveSpeed = 3.2f;
        public const float JumpForce = 7.0f;
        public const float Gravity = 20.0f;
        public const float GroundY = 0f;

        #endregion

        #region --- 别人位置平滑 / 跟服务器对齐 ---

        public const float SnapshotInterpolationDelay = 0.1f;
        public const float TeleportSnapDistance = 10f;
        public const float ReconcileIgnoreDistance = 0.35f;
        public const float ReconcileSnapDistance = 2.0f;
        /// <summary>站着不动时，别人位置最多隔这么久再发一次。</summary>
        public const float IdleSnapshotInterval = 0.5f;

        #endregion

        #region --- Skill IDs ---

        // 编号要和配表 Skill_{id}.json、网络包对上，别改。
        // 英雄单位 01
        public const int SkillId_NormalAttack = 11000101;      // 近战普攻 形
        public const int SkillId_Flash = 10000102;             // 闪现
        public const int SkillId_HeroRangedAttack = 10100103;  // 远程普攻 弹
        public const int SkillId_Fireball = 10110104;          // 火焰烙印 弹+Buff
        public const int SkillId_Cleave = 11010105;            // 旋风斩击 形+Buff
        public const int SkillId_Charge = 11010106;            // 野蛮冲撞 形+Buff
        public const int SkillId_TripleSlash = 11010107;       // 狂风绝息 形+Buff
        public const int SkillId_ArcaneStorm = 11010108;       // 冰川风暴（凤凰 R）
        public const int SkillId_PowerStrike = 11010109;       // 残虐猛击（塞恩 Q）
        public const int SkillId_TripleBolt = 10110110;        // 万箭齐发（寒冰 W）
        /// <summary>星辰凝滞：点地黑洞，结束时眩晕。</summary>
        public const int SkillId_PhoenixStorm = 11010111;
        public const int SkillId_RiftCombo = 11010201;
        /// <summary>光辉 R：终极闪光。</summary>
        public const int SkillId_LuxBolt = 11010202;
        public const int SkillId_Judgement = 11010203;
        public const int SkillId_HolySanctuary = 11010204;
        public const int SkillId_Execute = 11010205;
        /// <summary>伊泽 R：精准弹幕。</summary>
        public const int SkillId_EzrealBarrage = 11010206;
        // 小兵 / 塔
        public const int SkillId_MinionMeleeAttack = 21000101;
        public const int SkillId_MinionRangedAttack = 20100201;
        public const int SkillId_SiegeMinionAttack = 20100301;
        public const int SkillId_TowerAttack = 40100101;
        /// <summary>野怪普攻。伤害走自己的技能表。</summary>
        public const int SkillId_MonsterAttack = 31000101;
        /// <summary>Boss 普攻。</summary>
        public const int SkillId_BossAttack = 31000102;

        #endregion

        #region --- 弹道 ---

        // 编号要和弹道表、网络生成包对上。
        public const int ProjectileId_HeroBolt = 2003;
        public const int ProjectileId_MinionBolt = 2004;
        public const int ProjectileId_SiegeBolt = 2005;
        public const int ProjectileId_TowerBolt = 2006;
        public const int ProjectileId_Fireball = 2101;
        public const int ProjectileId_TripleBolt = 2102;
        public const int ProjectileId_RiftBolt = 2103;
        public const int ProjectileId_LuxBolt = 2104;
        public const int ProjectileId_EzrealBarrage = 2105;

        #endregion

        #region --- Buff ---

        // 编号要和 Buff 表对上。
        /// <summary>通用持续伤害模板。数值可由施法方填写。</summary>
        public const int BuffId_DotDamage = 1002;
        public const int BuffId_Stun = 3001;
        public const int BuffId_Slow = 3002;
        public const int BuffId_Airborne = 3003;
        public const int BuffId_Root = 3004;
        public const int BuffId_Silence = 3005;
        public const int BuffId_Burn = 3006;
        public const int BuffId_SuperArmor = 3007;
        public const int BuffId_ShieldWard = 3008;
        public const int BuffId_ArmorBreak = 3009;
        public const int BuffId_RootLock = 3010;
        public const int BuffId_HeavySlow = 3011;

        #endregion

        #region --- 技能杂项 / 复活 ---

        /// <summary>技能表没写射程时用这个。</summary>
        public const float DefaultCastRange = 6f;
        /// <summary>走进射程再放：最少走这么远，再按射程打折，避免贴脸停不住。</summary>
        public const float CastApproachMin = 0.35f;
        public const float CastApproachMul = 0.92f;
        /// <summary>提前按下的技能能记住多久（秒）。</summary>
        public const float SkillInputBufferTtl = 0.3f;
        /// <summary>动画前摇默认秒数。算伤害时机用配表里的出手延迟。</summary>
        public const float DefaultHitDelay = 0.1f;
        public const float DefaultMaxHp = 520f;

        public const float PlayerRespawnBase = 4f;
        public const float PlayerRespawnPerMinute = 1f;
        public const float PlayerRespawnCap = 12f;

        /// <summary>对局越久，复活等得越久，最长不超过上限。</summary>
        public static float CalcPlayerRespawnDelay(float matchElapsed)
        {
            float minutes = matchElapsed / 60f;
            if (minutes < 0f) minutes = 0f;
            float delay = PlayerRespawnBase + minutes * PlayerRespawnPerMinute;
            return delay > PlayerRespawnCap ? PlayerRespawnCap : delay;
        }

        #endregion

        #region --- 野怪 AI ---

        public const float MonsterAggroRange = 8f;
        public const float MonsterLeashRange = 14f;
        public const float MonsterPatrolRadius = 4f;
        public const float MonsterMoveSpeed = 2.8f;
        public const float MonsterHpRegenPerSec = 20f;
        public const float MonsterSnapshotInterval = 0.1f;

        #endregion

        #region --- 训练假人 ---

        public const float TrainingDummyHp = 2500f;
        public const float TrainingDummyRegenPerSec = 120f;
        public const int TrainingDummyCountPerSide = 3;
        public const float TrainingDummyAlongOffset = 6f;
        public const float TrainingDummyAcrossSpacing = 3.2f;

        #endregion
    }
}
