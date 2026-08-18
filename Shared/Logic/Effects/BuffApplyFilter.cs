namespace Shared
{
    /// <summary>这条 Buff 能不能套上（看阵营和当前状态）。概率由外面掷。</summary>
    public static class BuffApplyFilter
    {
        public static bool RelationMatches(
            TargetRelation relation,
            long casterId,
            int casterTeam,
            long recipientId,
            int recipientTeam)
        {
            bool self = casterId != 0 && casterId == recipientId;
            bool enemy = !self
                        && casterTeam != ETeamId.None
                        && recipientTeam != ETeamId.None
                        && casterTeam != recipientTeam;
            bool ally = !self && !enemy
                        && casterTeam != ETeamId.None
                        && casterTeam == recipientTeam;

            return relation switch
            {
                TargetRelation.Self => self,
                TargetRelation.Ally => ally,
                TargetRelation.Enemy => enemy,
                _ => false
            };
        }

        /// <summary>目标当前状态是否符合要求。配表编号和运行时不是同一套，这里会转换。</summary>
        public static bool StatusMatches(int recipientStateMask, int requireStatusFlags)
        {
            if (requireStatusFlags == 0) return true;
            int need = (int)EntityStateMachine.StatusFlagsToTag(requireStatusFlags);
            if (need == 0) return true;
            return (recipientStateMask & need) == need;
        }

        /// <summary>这条规则能不能套上（不含概率）。</summary>
        public static bool Matches(
            BuffApplySpec rule,
            long casterId,
            int casterTeam,
            long recipientId,
            int recipientTeam,
            int recipientStatusMask)
        {
            if (rule == null || rule.BuffId == 0) return false;
            if (!RelationMatches(rule.TargetRelation, casterId, casterTeam, recipientId, recipientTeam))
                return false;
            return StatusMatches(recipientStatusMask, rule.RequireStatusFlags);
        }

        /// <summary>找出所有能套上的规则。结果只往里加，不清空。</summary>
        public static void CollectMatching(
            BuffApplySpec[] rules,
            long casterId,
            int casterTeam,
            long recipientId,
            int recipientTeam,
            int recipientStatusMask,
            System.Collections.Generic.List<BuffApplySpec> into)
        {
            if (rules == null || into == null) return;
            for (int i = 0; i < rules.Length; i++)
            {
                var r = rules[i];
                if (!Matches(r, casterId, casterTeam, recipientId, recipientTeam, recipientStatusMask))
                    continue;
                into.Add(r);
            }
        }
    }
}
