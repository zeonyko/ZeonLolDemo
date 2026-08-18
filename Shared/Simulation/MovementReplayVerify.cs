using System.Collections.Generic;

namespace Shared
{
    /// <summary>离线检查：从服务器确认的位置，把还没确认的移动再走一遍，应和本地先动重合。</summary>
    public static class MovementReplayVerify
    {
        private struct Pending
        {
            public uint Seq;
            public float Mx, Mz, Dt;
        }

        /// <summary>跑完全部用例；report 是各组误差摘要。</summary>
        public static bool RunAll(out string report)
        {
            bool ok1 = CaseStationaryMotion(out var d1);
            bool ok2 = CaseSameDirMotionContinue(out var d2);
            bool ok3 = CaseReverseAfterMotion(out var d3);
            report = $"{d1} | {d2} | {d3}";
            return ok1 && ok2 && ok3;
        }

        /// <summary>站住放位移技：确认到位移提交后没有未确认移动 → 不回拉。</summary>
        public static bool CaseStationaryMotion(out string detail)
        {
            var origin = MovementState.FromPosition(10f, 0f, 10f);
            const float dist = 4f;
            var afterMotion = MovementSimulator.ApplySkillMotion(origin, 0f, 1f, dist);

            var pending = new List<Pending>();
            var corrected = Replay(afterMotion, pending, ack: 5);
            float err = Dist(afterMotion, corrected);
            detail = $"stationary err={err:F5}";
            return err <= GameConstants.ReconcileIgnoreDistance;
        }

        /// <summary>同向位移后再往前走：服务器落点再补未确认移动，应等于本地先动。</summary>
        public static bool CaseSameDirMotionContinue(out string detail)
        {
            var origin = MovementState.FromPosition(0f, 0f, 0f);
            var authLanding = MovementSimulator.ApplySkillMotion(origin, 0f, 1f, 4f);

            var pending = new List<Pending>
            {
                Move(11, 0f, 1f, 0.05f),
                Move(12, 0f, 1f, 0.05f),
                Move(13, 0f, 1f, 0.05f),
            };

            var local = authLanding;
            foreach (var p in pending)
                local = MovementSimulator.Simulate(local, new MoveInput { MoveX = p.Mx, MoveZ = p.Mz, DeltaTime = p.Dt });

            var corrected = Replay(authLanding, pending, ack: 10);
            float err = Dist(local, corrected);
            detail = $"same-dir err={err:F5}";
            return err <= GameConstants.ReconcileIgnoreDistance;
        }

        /// <summary>位移后立刻反向：再走反向输入应稳定。</summary>
        public static bool CaseReverseAfterMotion(out string detail)
        {
            var origin = MovementState.FromPosition(0f, 0f, 0f);
            var authLanding = MovementSimulator.ApplySkillMotion(origin, 0f, 1f, 4f);

            var pending = new List<Pending>
            {
                Move(21, 0f, -1f, 0.05f),
                Move(22, 0f, -1f, 0.05f),
            };

            var local = authLanding;
            foreach (var p in pending)
                local = MovementSimulator.Simulate(local, new MoveInput { MoveX = p.Mx, MoveZ = p.Mz, DeltaTime = p.Dt });

            var corrected = Replay(authLanding, pending, ack: 20);
            float err = Dist(local, corrected);
            detail = $"reverse err={err:F5}";
            return err <= GameConstants.ReconcileIgnoreDistance;
        }

        private static Pending Move(uint seq, float mx, float mz, float dt) =>
            new Pending { Seq = seq, Mx = mx, Mz = mz, Dt = dt };

        /// <summary>从服务器落点把还没确认的输入再走一遍。</summary>
        private static MovementState Replay(MovementState auth, List<Pending> pending, uint ack)
        {
            var state = auth;
            for (int i = 0; i < pending.Count; i++)
            {
                var p = pending[i];
                if (p.Seq <= ack) continue;
                state = MovementSimulator.Simulate(state, new MoveInput
                {
                    MoveX = p.Mx,
                    MoveZ = p.Mz,
                    DeltaTime = p.Dt
                });
            }
            return state;
        }

        private static float Dist(MovementState a, MovementState b) =>
            MovementSimulator.Distance3D(a.PosX, a.PosY, a.PosZ, b.PosX, b.PosY, b.PosZ);
    }
}
