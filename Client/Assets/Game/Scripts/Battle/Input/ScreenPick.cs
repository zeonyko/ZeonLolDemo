using UnityEngine;

namespace Client.Battle
{
    /// <summary>点屏幕附近的敌人。逻辑索敌不走这里。</summary>
    public static class ScreenPick
    {
        public const float RadiusPx = 42f;
        public const float MobileRadiusPx = 76f;

        public static bool TryPickEnemy(
            Camera cam, long localPlayerId, Vector2 screenPos, out long entityId)
        {
            float radius = Application.isMobilePlatform ? MobileRadiusPx : RadiusPx;
            return TryPickEnemy(cam, localPlayerId, screenPos, radius, out entityId);
        }

        public static bool TryPickEnemy(
            Camera cam,
            long localPlayerId,
            Vector2 screenPos,
            float screenRadiusPx,
            out long entityId)
        {
            entityId = 0;
            if (cam == null) return false;

            int selfTeam = 0;
            if (BattleCache.Instance != null && BattleCache.Instance.TryGet(localPlayerId, out var self))
                selfTeam = self.TeamId;

            float best = screenRadiusPx * screenRadiusPx;
            long bestId = 0;
            var all = BattleCache.Instance?.GetAll();
            if (all == null) return false;

            foreach (var kv in all)
            {
                var data = kv.Value;
                if (data == null || data.Id == 0 || data.Id == localPlayerId) continue;
                if (data.Hp <= 0f) continue;
                if (!SkillTargeting.AreEnemies(selfTeam, data.TeamId)) continue;
                if (!SkillTargeting.IsSelectable(data.EntityType)) continue;
                if (!SkillTargeting.TryGetWorldPos(data.Id, out Vector3 tgtPos)) continue;

                Vector3 sp = cam.WorldToScreenPoint(tgtPos + Vector3.up);
                if (sp.z < 0.1f) continue;

                float dx = sp.x - screenPos.x;
                float dy = sp.y - screenPos.y;
                float d2 = dx * dx + dy * dy;
                if (d2 > best) continue;
                best = d2;
                bestId = data.Id;
            }

            if (bestId == 0) return false;
            entityId = bestId;
            return true;
        }
    }
}
