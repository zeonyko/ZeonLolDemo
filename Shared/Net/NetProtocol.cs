using System;

namespace Shared
{
    /// <summary>网络协议：操作码、原因、收发包。两端读同一份。编号和字段名别改。</summary>

    // 认证 1xxx，战斗 2xxx。C2S 是客户端发给服务器，S2C 反过来。

    #region --- OpCode ---

    /// <summary>网络操作码。编号别改。</summary>
    public enum EOpCode
    {
        None = 0,

        // 认证 11xx
        C2S_Auth_LoginReq = 1101, // 登录请求
        S2C_Auth_LoginRsp = 1102, // 登录响应

        // 战斗·时间 21xx
        C2S_Battle_SyncTimeCmd = 2101, // 时间对齐请求
        S2C_Battle_SyncTimePacket = 2102, // 时间对齐响应（算来回延迟）

        // 战斗·实体 22xx
        S2C_Battle_SyncEntitiesPacket = 2201, // 场景单位列表
        S2C_Battle_EntityStatePacket = 2202, // 单位出生/销毁/死亡

        // 战斗·位置 23xx
        C2S_Battle_InputCmd = 2301, // 移动输入（可多帧冗余）
        S2C_Battle_ReconcilePacket = 2302, // 把本地位置跟服务器对齐
        S2C_Battle_RemoteSnapshotPacket = 2303, // 别人当前位置
        S2C_Battle_ForceRelocatePacket = 2304, // 服务器硬拉位置（复活/切场景/强拉）

        // 战斗·技能 24xx
        C2S_Battle_SkillCastCmd = 2401, // 请求施法
        S2C_Battle_SkillCastPacket = 2402, // 起手/前摇广播
        C2S_Battle_SkillCancelCmd = 2403, // 主动取消施法
        S2C_Battle_SkillCancelPacket = 2404, // 打断广播
        S2C_Battle_SkillHitPacket = 2405, // 算出伤害/效果
        S2C_Battle_ProjectileSpawnPacket = 2406, // 服务器生成弹道
        S2C_Battle_ProjectileDespawnPacket = 2407, // 服务器销毁弹道

        // 战斗·对局 25xx
        S2C_Battle_MatchResultPacket = 2501, // 算出胜负
        S2C_Battle_MatchRestartPacket = 2502, // 地图重建（随后 SyncEntities；若直接开战会再发 MatchStart）
        C2S_Battle_RematchReadyCmd = 2503, // 大厅/再战准备或取消
        S2C_Battle_RematchReadyPacket = 2504, // 准备人数同步
        S2C_Battle_MatchLobbyPacket = 2505, // 进入等待准备（开战前）
        S2C_Battle_MatchStartPacket = 2506, // 全员就绪，正式开战
    }

    /// <summary>单位出生、销毁、死亡、属性同步。</summary>
    public enum EEntityStateType
    {
        Spawn = 1, // 出生
        Destroy = 2, // 销毁
        Dead = 3, // 死亡
        AttrSync = 4, // 同步血量等，不重建单位
    }

    /// <summary>客户端主动取消施法原因。</summary>
    public enum EBattle_CancelCastReason
    {
        Move = 1, // 移动打断
        Dodge = 2, // 闪避打断
        ChargeRelease = 3, // 松开蓄力
    }

    /// <summary>服务器打断或拒绝施法的原因。Rejected 和 Illegal 退冷却的方式不同，别混用。</summary>
    public enum EBattle_SkillCancelNotifyReason
    {
        ActiveCancel = 1, // 主动取消
        ControlInterrupt = 2, // 受控打断
        Illegal = 3, // 已经收下后又作废（死亡/位移不合法等）
        Rejected = 4, // 服务器没通过。没记冷却，本地先扣的冷却要还回去
    }

    /// <summary>位置跟服务器对齐的原因。</summary>
    public enum EBattle_ReconcileReason
    {
        PredictionError = 1, // 本地先动对不上
        Collision = 2, // 穿墙碰撞
        IllegalSkillMotion = 3, // 技能位移非法
        HardControl = 4, // 硬直控制
    }

    /// <summary>服务器硬拉位置的原因。技能自己位移不走这条。</summary>
    public enum EBattle_RelocateReason
    {
        Respawn = 1,
        SceneChange = 2,
        ForcePull = 3,
        /// <summary>被技能击退。</summary>
        Knockback = 4,
    }

    /// <summary>命中结果标记。</summary>
    [Flags]
    public enum EBattle_HitFlags
    {
        None = 0,
        Crit = 1 << 0, // 暴击
        Dodge = 1 << 1, // 闪避
        Block = 1 << 2, // 格挡
        Invincible = 1 << 3, // 无敌
        Lethal = 1 << 4, // 致命
        /// <summary>不播受击表现（飘字/动作/光圈）。看单位配置，不是无敌。</summary>
        NoFeedback = 1 << 5,
    }

    /// <summary>弹道消失原因。</summary>
    public enum EBattle_ProjectileDespawnReason
    {
        Expired = 1, // 超距 / 目标失效
        HitCap = 2, // 命中上限
        Cancelled = 3, // 取消销毁
    }

    #endregion

    #region --- 通用结构 ---

    /// <summary>网络用的三维坐标。不依赖 Unity。</summary>
    [Serializable]
    public class Vector3Data
    {
        public float X;
        public float Y;
        public float Z;

        public static Vector3Data Of(float x, float y, float z) =>
            new Vector3Data { X = x, Y = y, Z = z };

        public static Vector3Data Zero => new Vector3Data();
    }

    /// <summary>网络用的位置和朝向。</summary>
    [Serializable]
    public class TransformData
    {
        public Vector3Data Position;
        public Vector3Data Rotation;

        public static TransformData Of(float px, float py, float pz, float rx = 0f, float ry = 0f, float rz = 0f) =>
            new TransformData
            {
                Position = Vector3Data.Of(px, py, pz),
                Rotation = Vector3Data.Of(rx, ry, rz)
            };
    }

    /// <summary>打中一个人的结果。</summary>
    [Serializable]
    public class SkillHitEntry
    {
        public long TargetId;
        public int Damage;
        public uint HitFlags; // EBattle_HitFlags
        public Vector3Data KnockbackDir;
        /// <summary>攻击类型。客户端用来查受击反馈。</summary>
        public string AttackType = "";
    }

    /// <summary>单位身上一条 Buff 的当前状态（还剩多久）。</summary>
    [Serializable]
    public class BuffInstanceData
    {
        public int BuffId;
        public int StackCount = 1;
        public float RemainingDuration;
        public long SourceEntityId;
        public int SourceSkillId;
    }

    /// <summary>场景里一个单位的同步数据。</summary>
    [Serializable]
    public class EntityData
    {
        public long Id;
        public string Name;
        public float PosX;
        public float PosY;
        public float PosZ;
        public float Hp;
        public float MaxHp;
        public int EntityType; // EEntityType
        public int TeamId; // ETeamId
        /// <summary>身上的 Buff。出生和属性同步时一起发。</summary>
        public BuffInstanceData[] Buffs = Array.Empty<BuffInstanceData>();
    }

    /// <summary>单帧客户端输入。普通移动和闪现/冲锋提交共用这个结构。</summary>
    [Serializable]
    public class SingleFrameInput
    {
        public uint Tick;
        public Vector3Data MoveIntent; // MotionCommit 时是闪现方向
        public uint InputFlags; // SyncContract.InputFlags
        public float DeltaTime; // 要和本地先动用的一致
        /// <summary>闪现提交时的距离；普通移动帧为 0。</summary>
        public float MotionDistance;
        public TransformData PredictedTf;
    }

    /// <summary>别人当前状态的一条。</summary>
    [Serializable]
    public class EntitySnapshot
    {
        public long EntityId;
        public TransformData Transform;
        public Vector3Data Velocity;
        public uint StateFlags;
        /// <summary>1 = 位置硬切（别人不平滑）；0 = 连续移动可平滑。</summary>
        public int HardSnap;
    }

    #endregion

    #region --- 认证 ---

    /// <summary>登录请求。</summary>
    [Serializable]
    public class C2S_Auth_LoginReq
    {
        public string PlayerName;
    }

    /// <summary>登录响应。</summary>
    [Serializable]
    public class S2C_Auth_LoginRsp
    {
        public long EntityId;
        public string PlayerName;
        public float PosX;
        public float PosY;
        public float PosZ;
        public int TeamId;
    }

    #endregion

    #region --- 战斗·时间 ---

    /// <summary>时间对齐请求。</summary>
    [Serializable]
    public class C2S_Battle_SyncTimeCmd
    {
        public long ClientSendTimestamp;
    }

    /// <summary>时间对齐响应（回传客户端戳以算来回延迟）。</summary>
    [Serializable]
    public class S2C_Battle_SyncTimePacket
    {
        public long ClientSendTimestamp;
        public long ServerTime;
        public uint ServerTick;
    }

    #endregion

    #region --- 战斗·实体 ---

    /// <summary>全量/场景单位列表。</summary>
    [Serializable]
    public class S2C_Battle_SyncEntitiesPacket
    {
        public EntityData[] Entities;
    }

    /// <summary>单个单位状态变更。</summary>
    [Serializable]
    public class S2C_Battle_EntityStatePacket
    {
        public long EntityId;
        public int StateType; // EEntityStateType
        public EntityData EntityData;
    }

    #endregion

    #region --- 战斗·位置 ---

    /// <summary>移动输入（含历史冗余帧）。</summary>
    [Serializable]
    public class C2S_Battle_InputCmd
    {
        public long EntityId;
        public uint LatestClientTick;
        public SingleFrameInput[] HistoryInputs;
    }

    /// <summary>把本地位置跟服务器对齐。AckClientTick 是已经模拟完哪一帧，不是按键序号。</summary>
    [Serializable]
    public class S2C_Battle_ReconcilePacket
    {
        /// <summary>服务器已经模拟完的最后一帧，不是按键序号。</summary>
        public uint AckClientTick;
        /// <summary>模拟完那一帧后的正确位置。</summary>
        public TransformData CorrectTf;
        public Vector3Data Velocity;
        public uint ReconcileReason; // EBattle_ReconcileReason
        public int IsGrounded;
    }

    /// <summary>别人当前位置列表。</summary>
    [Serializable]
    public class S2C_Battle_RemoteSnapshotPacket
    {
        public uint ServerTick;
        public EntitySnapshot[] EntitySnapshots;
    }

    /// <summary>服务器硬拉位置（复活/切场景等）。</summary>
    [Serializable]
    public class S2C_Battle_ForceRelocatePacket
    {
        public long EntityId;
        public TransformData TargetTf;
        public uint RelocateReason; // EBattle_RelocateReason
    }

    #endregion

    #region --- 战斗·技能 ---

    /// <summary>请求施法。</summary>
    [Serializable]
    public class C2S_Battle_SkillCastCmd
    {
        public int SkillId;
        public uint ClientTick;
        public TransformData CasterTransform;
        public Vector3Data TargetPosition;
        public long TargetId;
    }

    /// <summary>起手/前摇广播。</summary>
    [Serializable]
    public class S2C_Battle_SkillCastPacket
    {
        public long CasterId;
        public int SkillId;
        public uint ServerTick;
        public Vector3Data TargetPosition;
        public long TargetId;
    }

    /// <summary>主动取消施法。</summary>
    [Serializable]
    public class C2S_Battle_SkillCancelCmd
    {
        public int SkillId;
        public uint CancelReason; // EBattle_CancelCastReason
        public uint ClientTick;
    }

    /// <summary>打断/拒绝施法广播。</summary>
    [Serializable]
    public class S2C_Battle_SkillCancelPacket
    {
        public long CasterId;
        public int SkillId;
        public uint CancelReason; // EBattle_SkillCancelNotifyReason
        public uint InterruptTick;
        /// <summary>对应按下时那一帧。0 表示不知道（旧包）。用来对上多段/连招。</summary>
        public uint ClientTick;
    }

    /// <summary>算出伤害/效果（可以谁都没打中）。</summary>
    [Serializable]
    public class S2C_Battle_SkillHitPacket
    {
        public long CasterId;
        public int SkillId;
        public uint HitIndex;
        public SkillHitEntry[] Hits;
    }

    /// <summary>服务器生成弹道。</summary>
    [Serializable]
    public class S2C_Battle_ProjectileSpawnPacket
    {
        public long ProjectileInstanceId;
        public int ProjectileId;
        public int SkillId;
        public long CasterId;
        public Vector3Data Origin;
        public Vector3Data Dir;
        public long TargetId;
        public float Speed;
        public float MaxDistance;
        public float Width;
        public float SpawnServerTime;
    }

    /// <summary>服务器销毁弹道。</summary>
    [Serializable]
    public class S2C_Battle_ProjectileDespawnPacket
    {
        public long ProjectileInstanceId;
        public uint Reason; // EBattle_ProjectileDespawnReason
        public int SkillId;
        /// <summary>1=落地要炸，客户端在这个位置播爆炸。</summary>
        public int HasImpact;
        public Vector3Data ImpactPos;
    }

    #endregion

    #region --- 战斗·对局 ---

    /// <summary>对局谁赢了。</summary>
    [Serializable]
    public class S2C_Battle_MatchResultPacket
    {
        public int WinnerTeamId; // ETeamId
        public float MatchDuration;
        public long DestroyedCrystalId;
    }

    /// <summary>地图重建信号（随后 SyncEntities）。</summary>
    [Serializable]
    public class S2C_Battle_MatchRestartPacket
    {
    }

    /// <summary>再战准备/取消。</summary>
    [Serializable]
    public class C2S_Battle_RematchReadyCmd
    {
        /// <summary>1=准备，0=取消准备。</summary>
        public int Ready;
    }

    /// <summary>准备人数同步。</summary>
    [Serializable]
    public class S2C_Battle_RematchReadyPacket
    {
        public int ReadyCount;
        public int TotalCount;
        public long[] ReadyEntityIds;
    }

    /// <summary>进入开战前大厅。</summary>
    [Serializable]
    public class S2C_Battle_MatchLobbyPacket
    {
    }

    /// <summary>全员就绪正式开战。</summary>
    [Serializable]
    public class S2C_Battle_MatchStartPacket
    {
    }

    #endregion
}
