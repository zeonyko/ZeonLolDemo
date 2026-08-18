using Client.Network;
using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>战斗网络：收包写入数据、刷新外观、转交给技能和同步。不管具体怎么播。</summary>
    public class BattleNetHandler
    {
        #region 字段
        private readonly BattleSession _session;
        private bool _bound;
        private float _nextSyncTime;
        private const float SyncIntervalSeconds = 2f;
        #endregion

        #region 构造
        public BattleNetHandler(BattleSession session)
        {
            _session = session;
        }
        #endregion

        #region 绑定 / 解绑网络回调
        public void Bind(NetworkManager net)
        {
            if (_bound || net == null) return;

            // 实体 / 场景同步
            net.RegisterHandler<S2C_Battle_SyncEntitiesPacket>(EOpCode.S2C_Battle_SyncEntitiesPacket, OnSceneSyncEntities);
            net.RegisterHandler<S2C_Battle_EntityStatePacket>(EOpCode.S2C_Battle_EntityStatePacket, OnEntityStatePacket);

            // 位置跟服务器对齐 / 强制归位
            net.RegisterHandler<S2C_Battle_ReconcilePacket>(EOpCode.S2C_Battle_ReconcilePacket, OnLocalReconcile);
            net.RegisterHandler<S2C_Battle_RemoteSnapshotPacket>(EOpCode.S2C_Battle_RemoteSnapshotPacket, OnRemoteSnapshot);
            net.RegisterHandler<S2C_Battle_ForceRelocatePacket>(EOpCode.S2C_Battle_ForceRelocatePacket, OnForceRelocate);

            // 技能 / 弹道
            net.RegisterHandler<S2C_Battle_SkillHitPacket>(EOpCode.S2C_Battle_SkillHitPacket, OnSkillHitPacket);
            net.RegisterHandler<S2C_Battle_ProjectileSpawnPacket>(EOpCode.S2C_Battle_ProjectileSpawnPacket, OnProjectileSpawn);
            net.RegisterHandler<S2C_Battle_ProjectileDespawnPacket>(EOpCode.S2C_Battle_ProjectileDespawnPacket, OnProjectileDespawn);
            net.RegisterHandler<S2C_Battle_SkillCastPacket>(EOpCode.S2C_Battle_SkillCastPacket, OnSkillCastPacket);
            net.RegisterHandler<S2C_Battle_SkillCancelPacket>(EOpCode.S2C_Battle_SkillCancelPacket, OnSkillCancelPacket);

            // 时间同步
            net.RegisterHandler<S2C_Battle_SyncTimePacket>(EOpCode.S2C_Battle_SyncTimePacket, OnSyncTimePacket);

            // 对局流程（算胜负 / 大厅 / 开局 / 重开）
            net.RegisterHandler<S2C_Battle_MatchResultPacket>(EOpCode.S2C_Battle_MatchResultPacket, OnMatchResult);
            net.RegisterHandler<S2C_Battle_MatchRestartPacket>(EOpCode.S2C_Battle_MatchRestartPacket, OnMatchRestart);
            net.RegisterHandler<S2C_Battle_RematchReadyPacket>(EOpCode.S2C_Battle_RematchReadyPacket, OnRematchReadyPacket);
            net.RegisterHandler<S2C_Battle_MatchLobbyPacket>(EOpCode.S2C_Battle_MatchLobbyPacket, OnMatchLobby);
            net.RegisterHandler<S2C_Battle_MatchStartPacket>(EOpCode.S2C_Battle_MatchStartPacket, OnMatchStart);

            _bound = true;
        }

        public void Unbind(NetworkManager net)
        {
            if (!_bound || net == null) return;

            // 实体 / 场景同步
            net.UnregisterHandler<S2C_Battle_SyncEntitiesPacket>(EOpCode.S2C_Battle_SyncEntitiesPacket, OnSceneSyncEntities);
            net.UnregisterHandler<S2C_Battle_EntityStatePacket>(EOpCode.S2C_Battle_EntityStatePacket, OnEntityStatePacket);

            // 位置跟服务器对齐 / 强制归位
            net.UnregisterHandler<S2C_Battle_ReconcilePacket>(EOpCode.S2C_Battle_ReconcilePacket, OnLocalReconcile);
            net.UnregisterHandler<S2C_Battle_RemoteSnapshotPacket>(EOpCode.S2C_Battle_RemoteSnapshotPacket, OnRemoteSnapshot);
            net.UnregisterHandler<S2C_Battle_ForceRelocatePacket>(EOpCode.S2C_Battle_ForceRelocatePacket, OnForceRelocate);

            // 技能 / 弹道
            net.UnregisterHandler<S2C_Battle_SkillHitPacket>(EOpCode.S2C_Battle_SkillHitPacket, OnSkillHitPacket);
            net.UnregisterHandler<S2C_Battle_ProjectileSpawnPacket>(EOpCode.S2C_Battle_ProjectileSpawnPacket, OnProjectileSpawn);
            net.UnregisterHandler<S2C_Battle_ProjectileDespawnPacket>(EOpCode.S2C_Battle_ProjectileDespawnPacket, OnProjectileDespawn);
            net.UnregisterHandler<S2C_Battle_SkillCastPacket>(EOpCode.S2C_Battle_SkillCastPacket, OnSkillCastPacket);
            net.UnregisterHandler<S2C_Battle_SkillCancelPacket>(EOpCode.S2C_Battle_SkillCancelPacket, OnSkillCancelPacket);

            // 时间同步
            net.UnregisterHandler<S2C_Battle_SyncTimePacket>(EOpCode.S2C_Battle_SyncTimePacket, OnSyncTimePacket);

            // 对局流程（算胜负 / 大厅 / 开局 / 重开）
            net.UnregisterHandler<S2C_Battle_MatchResultPacket>(EOpCode.S2C_Battle_MatchResultPacket, OnMatchResult);
            net.UnregisterHandler<S2C_Battle_MatchRestartPacket>(EOpCode.S2C_Battle_MatchRestartPacket, OnMatchRestart);
            net.UnregisterHandler<S2C_Battle_RematchReadyPacket>(EOpCode.S2C_Battle_RematchReadyPacket, OnRematchReadyPacket);
            net.UnregisterHandler<S2C_Battle_MatchLobbyPacket>(EOpCode.S2C_Battle_MatchLobbyPacket, OnMatchLobby);
            net.UnregisterHandler<S2C_Battle_MatchStartPacket>(EOpCode.S2C_Battle_MatchStartPacket, OnMatchStart);

            _bound = false;
        }
        #endregion

        #region 心跳与时间同步
        public void Tick()
        {
            if (_session.LocalPlayerId == 0) return;
            if (Time.time < _nextSyncTime) return;

            _nextSyncTime = Time.time + SyncIntervalSeconds;
            SendSyncTimeCmd();
        }

        public static void SendInput(C2S_Battle_InputCmd cmd)
        {
            NetworkManager.Instance?.Send(EOpCode.C2S_Battle_InputCmd, cmd);
        }

        public static void SendSyncTimeCmd()
        {
            long nowMs = TickClock.Instance.NowClientMs();
            NetworkManager.Instance?.Send(EOpCode.C2S_Battle_SyncTimeCmd, new C2S_Battle_SyncTimeCmd
            {
                ClientSendTimestamp = nowMs
            });
        }

        public void OnLoginSuccess(S2C_Auth_LoginRsp loginRsp)
        {
            if (loginRsp == null) return;

            TickClock.Instance.Reset();
            _nextSyncTime = 0f;
            SendSyncTimeCmd();
        }

        private void OnSyncTimePacket(S2C_Battle_SyncTimePacket msg)
        {
            long nowMs = TickClock.Instance.NowClientMs();
            TickClock.Instance.OnSyncResponse(
                msg.ClientSendTimestamp, msg.ServerTime, msg.ServerTick, nowMs);
        }
        #endregion

        #region 实体状态同步（Spawn / AttrSync / Destroy / Dead）
        private void OnSceneSyncEntities(S2C_Battle_SyncEntitiesPacket msg)
        {
            if (_session.LocalPlayerId == 0)
            {
                Debug.LogWarning("[BattleNetHandler] SyncEntities 到达时尚未 Login，已忽略");
                return;
            }

            ApplySceneSync(msg);
        }

        private void OnEntityStatePacket(S2C_Battle_EntityStatePacket msg)
        {
            var cache = BattleCache.Instance;
            if (cache == null) return;

            switch ((EEntityStateType)msg.StateType)
            {
                case EEntityStateType.Spawn:
                    HandleEntitySpawn(cache, msg);
                    break;
                case EEntityStateType.AttrSync:
                    HandleEntityAttrSync(cache, msg);
                    break;
                case EEntityStateType.Destroy:
                    HandleEntityDestroy(cache, msg);
                    break;
                case EEntityStateType.Dead:
                    HandleEntityDead(msg);
                    break;
            }
        }

        private static void HandleEntitySpawn(BattleCache cache, S2C_Battle_EntityStatePacket msg)
        {
            if (msg.EntityData == null) return;

            if (msg.EntityData.Id == 0)
                msg.EntityData.Id = msg.EntityId;
            cache.ApplyEntity(msg.EntityData);
            EntityViewSync.Flush();
        }

        private void HandleEntityAttrSync(BattleCache cache, S2C_Battle_EntityStatePacket msg)
        {
            if (msg.EntityData == null) return;

            if (msg.EntityData.Id == 0)
                msg.EntityData.Id = msg.EntityId;
            cache.ApplyEntity(msg.EntityData, markViewDirty: false);

            if (msg.EntityData.Hp <= 0f) return;

            if (msg.EntityId == _session.LocalPlayerId)
                _session.LocalPlayerDead = false;

            // AttrSync 回血/复活时也还原死亡态（防漏 ForceRelocate）
            var living = EntityManager.Instance?.GetEntity(msg.EntityId);
            var combatView = living?.GetComponent<CombatViewComponent>();
            if (combatView != null && combatView.NeedsReviveRestore)
                combatView.RestoreAfterRevive();
        }

        private void HandleEntityDestroy(BattleCache cache, S2C_Battle_EntityStatePacket msg)
        {
            if (msg.EntityId == _session.LocalPlayerId) return;

            cache.RemoveEntity(msg.EntityId);
            EntityViewSync.Flush();
        }

        private static void HandleEntityDead(S2C_Battle_EntityStatePacket msg)
        {
            EntityViewSync.HandleDead(msg.EntityId);
        }

        private void ApplySceneSync(S2C_Battle_SyncEntitiesPacket sync)
        {
            if (sync?.Entities == null)
            {
                Debug.LogWarning("[BattleNetHandler] SyncEntities 解析失败");
                return;
            }

            var cache = BattleCache.Instance;
            if (cache == null) return;

            Debug.Log($"[BattleNetHandler] SyncEntities count={sync.Entities.Length}");
            foreach (var data in sync.Entities)
            {
                Debug.Log($"[BattleNetHandler]   entity Id={data.Id} Name={data.Name} Type={data.EntityType}");
                cache.ApplyEntity(data);
            }

            EntityViewSync.Flush();

            if (_session.LocalPlayerId != 0
                && BattleCache.Instance != null
                && BattleCache.Instance.TryGet(_session.LocalPlayerId, out var local)
                && local != null)
                _session.LocalTeamId = local.TeamId;
        }
        #endregion

        #region 技能施法 / 取消 / 命中包
        private void OnSkillCastPacket(S2C_Battle_SkillCastPacket msg)
        {
            if (msg == null) return;

            // 自己：这包就是服务器确认施法（确认本地冷却 / 继续播时间轴）
            if (msg.CasterId == _session.LocalPlayerId)
            {
                EntityManager.Instance?.GetEntity(msg.CasterId)
                    ?.GetComponent<SkillCastComponent>()
                    ?.OnCastAcknowledged(msg.SkillId);
                return;
            }

            var caster = EntityManager.Instance?.GetEntity(msg.CasterId);
            if (caster == null) return;

            Vector3 targetPos = msg.TargetPosition != null
                ? new Vector3(msg.TargetPosition.X, msg.TargetPosition.Y, msg.TargetPosition.Z)
                : caster.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;

            // 网络只转发；落点圈/施法特效由施法表现统一播
            caster.GetComponent<SkillCastComponent>()?.PlayRemoteCast(
                msg.SkillId, targetPos, msg.TargetId);
        }

        private void OnSkillCancelPacket(S2C_Battle_SkillCancelPacket msg)
        {
            if (msg == null) return;

            var caster = EntityManager.Instance?.GetEntity(msg.CasterId);
            var skillComp = caster?.GetComponent<SkillCastComponent>();
            if (skillComp == null) return;

            if (msg.CasterId == _session.LocalPlayerId)
            {
                Vector3 authPos = caster.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;
                skillComp.OnSkillCancel(msg.SkillId, msg.ClientTick, msg.CancelReason, authPos);
                return;
            }

            skillComp.OnRemoteCancel(msg.CancelReason);
        }

        private void OnSkillHitPacket(S2C_Battle_SkillHitPacket msg)
        {
            SkillHitPresenter.ProcessNetwork(msg);
        }
        #endregion

        #region 弹道生成 / 销毁包
        private void OnProjectileSpawn(S2C_Battle_ProjectileSpawnPacket msg)
        {
            ProjectileViewService.Instance?.OnSpawn(msg);
        }

        private void OnProjectileDespawn(S2C_Battle_ProjectileDespawnPacket msg)
        {
            ProjectileViewService.Instance?.OnDespawn(msg);
        }
        #endregion

        #region 位置同步 / 强制归位包
        private void OnLocalReconcile(S2C_Battle_ReconcilePacket msg)
        {
            EntityManager.Instance?.GetEntity(_session.LocalPlayerId)
                ?.GetComponent<PredictionMovementComponent>()
                ?.OnServerReconcile(msg);
        }

        private void OnRemoteSnapshot(S2C_Battle_RemoteSnapshotPacket msg)
        {
            if (msg.EntitySnapshots == null) return;

            foreach (var snap in msg.EntitySnapshots)
            {
                if (snap == null || snap.EntityId == _session.LocalPlayerId) continue;

                var entity = EntityManager.Instance?.GetEntity(snap.EntityId);
                if (entity == null) continue;

                Vector3 pos = snap.Transform?.Position != null
                    ? new Vector3(snap.Transform.Position.X, snap.Transform.Position.Y, snap.Transform.Position.Z)
                    : entity.GetComponent<TransformComponent>()?.Position ?? Vector3.zero;

                entity.GetComponent<RemotePlayerSyncComponent>()
                    ?.OnReceiveSnapshot(pos, snap.HardSnap != 0, snap.StateFlags);
            }
        }

        private void OnForceRelocate(S2C_Battle_ForceRelocatePacket msg)
        {
            if (msg.TargetTf?.Position == null) return;

            Vector3 pos = new Vector3(
                msg.TargetTf.Position.X,
                msg.TargetTf.Position.Y,
                msg.TargetTf.Position.Z);

            if (msg.EntityId == _session.LocalPlayerId)
            {
                HandleForceRelocateLocal(msg.EntityId, pos, msg.RelocateReason);
                return;
            }

            HandleForceRelocateRemote(msg.EntityId, pos, msg.RelocateReason);
        }

        /// <summary>自己被强制归位。复活时顺带清死亡态并还原外观。</summary>
        private void HandleForceRelocateLocal(long entityId, Vector3 pos, uint relocateReason)
        {
            EntityManager.Instance?.GetEntity(entityId)
                ?.GetComponent<PredictionMovementComponent>()
                ?.ApplyForceRelocate(pos, (EBattle_RelocateReason)relocateReason);

            if (relocateReason == (uint)EBattle_RelocateReason.Respawn)
                ClearLocalDeathAndRestoreRevive(entityId);
        }

        /// <summary>别人被强制归位。玩家立刻贴齐，其它实体也直接贴逻辑位置。</summary>
        private static void HandleForceRelocateRemote(long entityId, Vector3 pos, uint relocateReason)
        {
            var remote = EntityManager.Instance?.GetEntity(entityId);
            if (remote == null) return;

            // 玩家/小兵/木桩：立刻贴齐逻辑位置（服务器击退 / 强拉）
            if (remote.GetComponent<RemotePlayerSyncComponent>() != null)
                remote.GetComponent<RemotePlayerSyncComponent>().ApplyHardSnap(pos);
            else
            {
                remote.GetComponent<TransformComponent>()?.SnapPosition(pos);
                remote.GetComponent<ViewComponent>()?.SnapToLogic();
            }

            if (relocateReason == (uint)EBattle_RelocateReason.Respawn)
                remote.GetComponent<CombatViewComponent>()?.RestoreAfterRevive();
        }

        /// <summary>Session 清本地死亡标记，并还原复活后的 CombatView。</summary>
        private void ClearLocalDeathAndRestoreRevive(long entityId)
        {
            _session.LocalPlayerDead = false;
            _session.LocalRespawnEndsAt = -1f;
            EntityManager.Instance?.GetEntity(entityId)
                ?.GetComponent<CombatViewComponent>()
                ?.RestoreAfterRevive();
        }
        #endregion

        #region 对局流程包（算胜负 / 大厅 / 开局 / 重开）
        private void OnMatchResult(S2C_Battle_MatchResultPacket msg)
        {
            if (msg == null || _session == null) return;

            _session.MatchEnded = true;
            _session.MatchPlaying = false;
            _session.WinnerTeamId = msg.WinnerTeamId;
            _session.MatchDuration = msg.MatchDuration;

            int localTeam = _session.LocalTeamId;
            if (localTeam == 0
                && BattleCache.Instance != null
                && BattleCache.Instance.TryGet(_session.LocalPlayerId, out var data)
                && data != null)
            {
                localTeam = data.TeamId;
                _session.LocalTeamId = localTeam;
            }

            bool victory = localTeam != 0 && localTeam == msg.WinnerTeamId;
            BattleAssets.Run(CoShowResult(victory, msg.MatchDuration));
            Debug.Log($"[Battle] MatchResult winner={msg.WinnerTeamId} duration={msg.MatchDuration:F1}s victory={victory}");
        }

        private void OnMatchLobby(S2C_Battle_MatchLobbyPacket msg)
        {
            if (_session == null) return;
            _session.MatchEnded = false;
            _session.MatchPlaying = false;
            BattleAssets.Run(CoShowLobby());
            Debug.Log("[Battle] MatchLobby: waiting for ready");
        }

        static System.Collections.IEnumerator CoShowLobby()
        {
            yield return MatchResultPanel.EnsureAsync();
            MatchEvents.RaiseLobby();
        }

        static System.Collections.IEnumerator CoShowResult(bool victory, float duration)
        {
            yield return MatchResultPanel.EnsureAsync();
            MatchEvents.RaiseResult(victory, duration);
        }

        private void OnMatchStart(S2C_Battle_MatchStartPacket msg)
        {
            if (_session == null) return;
            MatchEvents.RaiseStart();
            _session.MatchEnded = false;
            _session.MatchPlaying = true;
            _session.WinnerTeamId = 0;
            _session.MatchDuration = 0f;
            _session.MatchElapsed = 0f;
            _session.LocalPlayerDead = false;
            _session.LocalRespawnEndsAt = -1f;
            Debug.Log("[Battle] MatchStart");
        }

        private void OnRematchReadyPacket(S2C_Battle_RematchReadyPacket msg)
        {
            MatchEvents.RaiseReadyState(msg);
        }

        private void OnMatchRestart(S2C_Battle_MatchRestartPacket msg)
        {
            if (_session == null) return;

            MatchEvents.RaiseHide();
            _session.MatchEnded = false;
            _session.MatchPlaying = false;
            _session.WinnerTeamId = 0;
            _session.MatchDuration = 0f;
            _session.MatchElapsed = 0f;
            _session.LocalPlayerDead = false;
            _session.LocalRespawnEndsAt = -1f;

            var cache = BattleCache.Instance;
            if (cache != null)
            {
                long keepId = _session.LocalPlayerId;
                var ids = new System.Collections.Generic.List<long>(cache.GetAll().Keys);
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i] != keepId)
                        cache.RemoveEntity(ids[i]);
                }
            }

            Playback.Instance?.Clear();
            ProjectileViewService.Instance?.Clear();
            EntityViewSync.Flush();
            Debug.Log("[Battle] MatchRestart: cleared world, awaiting SyncEntities / MatchLobby");
        }
        #endregion
    }
}
