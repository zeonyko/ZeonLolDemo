using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>按数据组装角色：挂组件、登记进管理器。</summary>
    public static class EntityFactory
    {
        public static Entity CreateLocalPlayer(EntityData data)
        {
            return CreateEntity(data, isLocalPlayer: true);
        }

        public static Entity CreateEntity(EntityData data)
        {
            return CreateEntity(data, isLocalPlayer: false);
        }

        public static Entity CreateStoryActor(EntityData data, Color color)
        {
            if (data == null || data.Id == 0) return null;

            string name = string.IsNullOrEmpty(data.Name) ? $"Story_{data.Id}" : data.Name;
            Entity entity = CreateShell(data, isLocalPlayer: false, forceGrounded: true, name);
            SeedBattleCache(data, name);
            AttachPresentation(
                entity, name, color, showName: true, bindFormalView: true, viewType: EEntityType.Player);
            EntityManager.Instance.RegisterEntity(entity);
            return entity;
        }

        static Entity CreateEntity(EntityData data, bool isLocalPlayer)
        {
            if (data == null || data.Id == 0) return null;

            int viewType = data.EntityType > 0 ? data.EntityType : EEntityType.Player;
            string name = string.IsNullOrEmpty(data.Name)
                ? BattleCache.ResolveEntityName(data)
                : data.Name;

            Entity entity = CreateShell(data, isLocalPlayer, forceGrounded: false, name);
            SeedBattleCache(data, name);

            Color color = GetTeamColor(viewType, isLocalPlayer, data.TeamId);
            bool showName = isLocalPlayer
                || viewType == EEntityType.Player
                || viewType == EEntityType.Monster
                || viewType == EEntityType.Boss
                || viewType == EEntityType.Barracks
                || viewType == EEntityType.MinionSuper;
            AttachPresentation(entity, name, color, showName, bindFormalView: true, viewType);

            AttachRoleComponents(entity, viewType, isLocalPlayer);
            EntityManager.Instance.RegisterEntity(entity);
            return entity;
        }

        static Entity CreateShell(EntityData data, bool isLocalPlayer, bool forceGrounded, string name)
        {
            Vector3 pos = ResolvePos(data);
            Entity entity = new Entity(data.Id);
            entity.IsLocalPlayer = isLocalPlayer;
            entity.Name = name;

            var transform = entity.AddComponent<TransformComponent>();
            transform.SetPosition(pos);
            transform.IsGrounded = forceGrounded || pos.y <= GameConstants.GroundY + 0.001f;
            return entity;
        }

        static void SeedBattleCache(EntityData data, string name)
        {
            float maxHp = data.MaxHp > 0f ? data.MaxHp : GameConstants.DefaultMaxHp;
            float hp = data.Hp >= 0f ? data.Hp : maxHp;
            BattleCache.Instance?.EnsureSeed(data.Id, name, hp, maxHp, data.TeamId, data.EntityType);
        }

        static void AttachPresentation(
            Entity entity, string name, Color color, bool showName, bool bindFormalView, int viewType)
        {
            entity.AddComponent<SkillCastComponent>();
            entity.AddComponent<StateComponent>();
            entity.AddComponent<AnimationComponent>();

            var view = entity.AddComponent<ViewComponent>();
            if (bindFormalView)
                BindView(view, viewType, color);
            else
                view.InitPlaceholderView(color, Vector3.one);

            // 先有动画再有外观，再绑 Animator，顺序不能反
            entity.GetComponent<AnimationComponent>()?.BindAnimator();

            entity.AddComponent<FXComponent>();
            entity.AddComponent<StatusFxComponent>();
            entity.AddComponent<BuffComponent>();
            entity.AddComponent<HeadUIComponent>().InitHeadText(name, color, showName);
            entity.AddComponent<CombatViewComponent>();
        }

        static void AttachRoleComponents(Entity entity, int viewType, bool isLocalPlayer)
        {
            if (isLocalPlayer)
            {
                entity.AddComponent<InputPlayerComponent>();
                entity.AddComponent<LocalPlayerMoveComponent>();
                entity.AddComponent<CameraFollowComponent>();
                entity.AddComponent<PredictionMovementComponent>();
                entity.AddComponent<SyncCompareComponent>();
                return;
            }

            if (viewType == EEntityType.Tower
                || viewType == EEntityType.Crystal
                || viewType == EEntityType.Barracks)
                return;

            entity.AddComponent<RemotePlayerSyncComponent>();
        }

        static void BindView(ViewComponent view, int viewType, Color teamColor)
        {
            if (view == null) return;

            view.InitPlaceholderView(teamColor, GetPlaceholderScale(viewType));

            string path = UnitViewCatalog.GetPrefabKey(viewType);
            if (string.IsNullOrEmpty(path))
                return;

            bool hero = viewType == EEntityType.Player;
            view.RequestFormalView(path, hero ? (Color?)null : teamColor);
        }

        static Vector3 ResolvePos(EntityData data)
        {
            Vector3 pos = new Vector3(data.PosX, data.PosY, data.PosZ);
            if (pos.y < GameConstants.GroundY)
                pos.y = GameConstants.GroundY;
            return pos;
        }

        static Color GetTeamColor(int viewType, bool isLocalPlayer, int teamId)
        {
            Color blue = new Color(0.18f, 0.48f, 0.98f);
            Color red = new Color(0.92f, 0.2f, 0.18f);
            Color teamColor = teamId == ETeamId.Red ? red : blue;

            if (isLocalPlayer) return teamColor;

            return viewType switch
            {
                EEntityType.Player => Color.Lerp(teamColor, Color.white, 0.18f),
                EEntityType.Monster => new Color(0.95f, 0.78f, 0.2f),
                EEntityType.Boss => new Color(0.72f, 0.12f, 0.55f),
                EEntityType.MinionMelee => Color.Lerp(teamColor, new Color(0.55f, 0.55f, 0.58f), 0.15f),
                EEntityType.MinionRanged => Color.Lerp(teamColor, Color.white, 0.22f),
                EEntityType.MinionSiege => Color.Lerp(teamColor, new Color(0.85f, 0.65f, 0.25f), 0.2f),
                EEntityType.MinionSuper => Color.Lerp(teamColor, new Color(1f, 0.85f, 0.35f), 0.28f),
                EEntityType.Tower => Color.Lerp(teamColor, new Color(0.42f, 0.44f, 0.48f), 0.22f),
                EEntityType.Crystal => Color.Lerp(teamColor, new Color(0.95f, 0.95f, 1f), 0.25f),
                EEntityType.Barracks => Color.Lerp(teamColor, new Color(0.42f, 0.44f, 0.48f), 0.22f),
                _ => Color.white
            };
        }

        static Vector3 GetPlaceholderScale(int viewType)
        {
            return viewType switch
            {
                EEntityType.Player => Vector3.one,
                EEntityType.Monster => Vector3.one * 1.2f,
                EEntityType.Boss => Vector3.one * 2.5f,
                EEntityType.MinionMelee => new Vector3(0.5f, 0.55f, 0.5f),
                EEntityType.MinionRanged => new Vector3(0.42f, 0.48f, 0.42f),
                EEntityType.MinionSiege => new Vector3(0.85f, 0.7f, 0.95f),
                EEntityType.MinionSuper => new Vector3(0.65f, 0.72f, 0.65f),
                EEntityType.Tower => new Vector3(1.8f, 2.4f, 1.8f),
                EEntityType.Crystal => new Vector3(2.4f, 2.8f, 2.4f),
                EEntityType.Barracks => new Vector3(1.8f, 2.4f, 1.8f),
                _ => Vector3.one
            };
        }
    }
}
