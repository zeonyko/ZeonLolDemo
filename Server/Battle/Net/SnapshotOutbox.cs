using System.Collections.Generic;
using Shared;

namespace Server.Battle
{
    /// <summary>一帧里脏了的位置攒成一个快照包。不补中间帧，不合成位置。</summary>
    public static class SnapshotOutbox
    {
        private static readonly Dictionary<long, EntitySnapshot> Pending = new Dictionary<long, EntitySnapshot>();
        private static readonly List<EntitySnapshot> FlushBuf = new List<EntitySnapshot>(64);

        public static void Enqueue(Entity entity, bool hardSnap = false)
        {
            var snap = SkillResultService.ToEntitySnapshot(entity, hardSnap);
            if (snap == null) return;

            if (Pending.TryGetValue(entity.Id, out var prev) && prev != null && prev.HardSnap != 0)
                snap.HardSnap = 1;
            Pending[entity.Id] = snap;
        }

        public static void Flush()
        {
            if (Pending.Count == 0) return;

            var net = BattleNetPublisher.Instance;
            if (net == null)
            {
                Pending.Clear();
                return;
            }

            FlushBuf.Clear();
            foreach (var kv in Pending)
            {
                if (kv.Value != null)
                    FlushBuf.Add(kv.Value);
            }
            Pending.Clear();
            if (FlushBuf.Count == 0) return;

            net.BroadcastAll(EOpCode.S2C_Battle_RemoteSnapshotPacket, new S2C_Battle_RemoteSnapshotPacket
            {
                ServerTick = BattleSystem.Instance?.ServerTick ?? 0,
                EntitySnapshots = FlushBuf.ToArray()
            });
        }

        public static void Clear()
        {
            Pending.Clear();
            FlushBuf.Clear();
        }
    }
}
