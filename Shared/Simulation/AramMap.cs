using System;

namespace Shared
{
    /// <summary>单位类型编号。要和配表、网络包对上。0 不用。</summary>
    public static class EEntityType
    {
        public const int Player = 1;
        public const int Monster = 2;
        public const int Boss = 3;
        public const int MinionMelee = 4;
        public const int MinionRanged = 5;
        public const int Tower = 6;
        public const int Crystal = 7;
        /// <summary>攻城兵（炮车）。</summary>
        public const int MinionSiege = 8;
        /// <summary>兵营位（内塔；摧毁后对侧出超级兵）。</summary>
        public const int Barracks = 9;
        /// <summary>超级兵。</summary>
        public const int MinionSuper = 10;
    }

    /// <summary>阵营：蓝 / 红。None 是无阵营。</summary>
    public static class ETeamId
    {
        public const int None = 0;
        public const int Blue = 1;
        public const int Red = 2;

        /// <summary>取对方阵营；无阵营时仍返回无阵营。</summary>
        public static int EnemyOf(int team)
        {
            if (team == Blue) return Red;
            if (team == Red) return Blue;
            return None;
        }
    }

    /// <summary>嚎哭深渊单线地图。推进轴/横向偏移转成斜向世界坐标。</summary>
    public static class AramMap
    {
        #region --- 地图大小 / 出兵节奏 ---

        public const float InvSqrt2 = 0.70710678118f;

        /// <summary>中线半长（推进方向可走范围）。</summary>
        public const float LaneHalfLength = 56f;
        /// <summary>中线半宽（横向可走范围）。</summary>
        public const float LaneHalfWidth = 9f;
        /// <summary>相机焦点半范围。</summary>
        public const float CameraHalfExtent = 48f;
        public const float WallThickness = 2.5f;
        /// <summary>相机焦点内缩边距。</summary>
        public const float CameraFocusMargin = 4f;

        public const float BlueCrystalAlong = -46f;
        public const float RedCrystalAlong = 46f;
        public const float BlueBarracksAlong = -31f;
        public const float RedBarracksAlong = 31f;
        public const float BlueTowerAlong = -16f;
        public const float RedTowerAlong = 16f;
        public const float BlueSpawnAlong = -50f;
        public const float RedSpawnAlong = 50f;

        // 血量看单位表；出兵节奏看地图战斗表。
        public static float MinionMoveSpeed => MapCombatCatalog.Current.MinionMoveSpeed;
        public static float MinionAggroRange => MapCombatCatalog.Current.MinionAggroRange;
        public static float TowerAttackRange => MapCombatCatalog.Current.TowerAttackRange;
        public static float TowerScanInterval => MapCombatCatalog.Current.TowerScanInterval;
        public static float MinionWaveInterval => MapCombatCatalog.Current.MinionWaveInterval;
        public static float MinionSpawnInterval => MapCombatCatalog.Current.MinionSpawnInterval;
        /// <summary>到达路点判定半径。</summary>
        public const float MinionLaneWaypointReach = 1.35f;
        /// <summary>小兵碰撞体半径。</summary>
        public const float MinionBodyRadius = 0.42f;
        /// <summary>推线软分离强度。</summary>
        public const float MinionSeparationStrength = 0.55f;
        /// <summary>攻击环槽位夹角（度）。</summary>
        public const float MinionEngageRingAngleDeg = 36f;
        /// <summary>单侧最大攻击槽位数 = 2*N+1 的 N。</summary>
        public const int MinionEngageSlotMax = 3;
        /// <summary>进入攻击槽判定距离。</summary>
        public const float MinionEngageSlotArrive = 0.45f;
        /// <summary>近战互斥排斥半径。</summary>
        public const float MinionEngageRepulseRadius = 0.95f;
        /// <summary>近战互斥排斥权重。</summary>
        public const float MinionEngageRepulseWeight = 1.1f;
        /// <summary>呼叫协助范围。</summary>
        public const float MinionCallForHelpRange = 5.5f;
        /// <summary>战斗记忆时长（脱离仇恨缓冲）。</summary>
        public const float MinionCombatMemory = 2.5f;
        /// <summary>强制仇恨持续时间。</summary>
        public const float MinionForcedAggroDuration = 3f;
        /// <summary>后排目标偏置距离（优先打远程）。</summary>
        public const float MinionBacklineTargetBias = 3.5f;
        public const float RangedProjectileSpeed = 16f;

        /// <summary>中线路点推进位置；0→N 为蓝方推进方向。</summary>
        public static readonly float[] LanePathAlong =
        {
            -41f, -31f, -16f, 0f, 16f, 31f, 46f
        };
        /// <summary>脱战绳索倍率（× 仇恨范围）。</summary>
        public const float MinionLeashMul = 1.25f;
        /// <summary>防御塔呼叫协助窗口。</summary>
        public const float TowerCallForHelpWindow = 3f;

        #endregion

        #region --- 推进坐标 ↔ 世界坐标 ---

        /// <summary>推进/横向 转成世界坐标。</summary>
        public static void LaneToWorld(float along, float across, out float x, out float z)
        {
            x = (along - across) * InvSqrt2;
            z = (along + across) * InvSqrt2;
        }

        /// <summary>世界坐标转成推进/横向。</summary>
        public static void WorldToLane(float x, float z, out float along, out float across)
        {
            along = (x + z) * InvSqrt2;
            across = (-x + z) * InvSqrt2;
        }

        #endregion

        #region --- 出生点 / 塔 / 基地 ---

        public static void GetPlayerSpawn(int teamId, out float x, out float z)
        {
            float along = teamId == ETeamId.Red ? RedSpawnAlong : BlueSpawnAlong;
            LaneToWorld(along, 0f, out x, out z);
        }

        /// <summary>按阵营取基地位置。</summary>
        public static void GetCrystalPos(int teamId, out float x, out float z)
        {
            float along = teamId == ETeamId.Red ? RedCrystalAlong : BlueCrystalAlong;
            LaneToWorld(along, 0f, out x, out z);
        }

        public static void GetTowerPos(int teamId, out float x, out float z)
        {
            float along = teamId == ETeamId.Red ? RedTowerAlong : BlueTowerAlong;
            LaneToWorld(along, 0f, out x, out z);
        }

        public static void GetBarracksPos(int teamId, out float x, out float z)
        {
            float along = teamId == ETeamId.Red ? RedBarracksAlong : BlueBarracksAlong;
            LaneToWorld(along, 0f, out x, out z);
        }

        /// <summary>按阵营取小兵出生点。</summary>
        public static void GetMinionSpawn(int teamId, float alongOffset, float across, out float x, out float z)
        {
            float baseAlong = teamId == ETeamId.Blue ? BlueCrystalAlong + 5f : RedCrystalAlong - 5f;
            float dir = teamId == ETeamId.Blue ? 1f : -1f;
            LaneToWorld(baseAlong + dir * alongOffset, across, out x, out z);
        }

        #endregion

        #region --- 兵线路点 ---

        public static int LanePathPointCount => LanePathAlong.Length;

        /// <summary>按阵营取路点推进位置。红方路点反过来。</summary>
        public static float GetLanePathAlong(int teamId, int pathIndex)
        {
            int n = LanePathAlong.Length;
            if (n <= 0) return 0f;
            if (pathIndex < 0) pathIndex = 0;
            if (pathIndex >= n) pathIndex = n - 1;
            if (teamId == ETeamId.Red)
                return LanePathAlong[n - 1 - pathIndex];
            return LanePathAlong[pathIndex];
        }

        /// <summary>取路点世界坐标。下标越界返回失败。</summary>
        public static bool TryGetLanePathWorld(int teamId, int pathIndex, out float x, out float z)
        {
            return TryGetLanePathWorld(teamId, pathIndex, 0f, out x, out z);
        }

        /// <summary>取路点世界坐标，可带横向偏移。</summary>
        public static bool TryGetLanePathWorld(int teamId, int pathIndex, float across, out float x, out float z)
        {
            if (pathIndex < 0 || pathIndex >= LanePathAlong.Length)
            {
                x = 0f;
                z = 0f;
                return false;
            }

            LaneToWorld(GetLanePathAlong(teamId, pathIndex), across, out x, out z);
            return true;
        }

        /// <summary>按当前位置解析下一推进路点下标。</summary>
        public static int ResolveLanePathIndex(int teamId, float x, float z)
        {
            int n = LanePathAlong.Length;
            if (n <= 0) return 0;

            WorldToLane(x, z, out float along, out _);
            float dir = teamId == ETeamId.Blue ? 1f : -1f;
            float ahead = MinionLaneWaypointReach * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float wp = GetLanePathAlong(teamId, i);
                if (dir * (wp - along) > ahead)
                    return i;
            }

            return n - 1;
        }

        #endregion

        #region --- 限制在地图内 ---

        /// <summary>把坐标限制在可行走走廊内。</summary>
        public static void ClampToLane(ref float x, ref float z, float radius = 0f)
        {
            WorldToLane(x, z, out float u, out float v);
            float limU = LaneHalfLength - Math.Max(0f, radius);
            float limV = LaneHalfWidth - Math.Max(0f, radius);
            if (limU < 1f) limU = 1f;
            if (limV < 1f) limV = 1f;
            if (u > limU) u = limU;
            else if (u < -limU) u = -limU;
            if (v > limV) v = limV;
            else if (v < -limV) v = -limV;
            LaneToWorld(u, v, out x, out z);
        }

        /// <summary>把相机焦点限制在地图内。</summary>
        public static void ClampCameraFocus(ref float x, ref float z)
        {
            WorldToLane(x, z, out float u, out float v);
            float lim = CameraHalfExtent - CameraFocusMargin;
            if (lim < 1f) lim = 1f;
            if (u > lim) u = lim;
            else if (u < -lim) u = -lim;
            if (v > lim) v = lim;
            else if (v < -lim) v = -lim;
            LaneToWorld(u, v, out x, out z);
        }

        #endregion

        #region --- 兵线节奏 ---

        /// <summary>该波兵线是否含攻城车（随波数递增出现频率）。</summary>
        public static bool WaveHasSiege(int waveIndex)
        {
            if (waveIndex < 3) return false;
            if (waveIndex <= 20) return waveIndex % 3 == 0;
            if (waveIndex <= 40) return waveIndex % 2 == 0;
            return true;
        }

        #endregion
    }
}
