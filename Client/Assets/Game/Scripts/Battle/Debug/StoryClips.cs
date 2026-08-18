using System.Collections.Generic;
using Shared;

namespace Client.Battle
{
    #region 剧本容器
    /// <summary>一段可命名的战斗剧本（指令时间轴）。</summary>
    public sealed class StoryClip
    {
        public string Name { get; }
        public int ActorCount { get; }
        public IReadOnlyList<StoryCommand> Commands { get; }

        public StoryClip(string name, List<StoryCommand> commands, int actorCount = 2)
        {
            Name = name;
            ActorCount = actorCount < 2 ? 2 : actorCount;
            commands.Sort((a, b) => a.AtTime.CompareTo(b.AtTime));
            Commands = commands;
        }
    }
    #endregion

    #region 内置剧本工厂
    /// <summary>内置战斗故事。GM 剧情页点播，本地演绎不发包。</summary>
    public static class StoryClips
    {
        const int AA = GameConstants.SkillId_NormalAttack;
        const int Flash = GameConstants.SkillId_Flash;
        const int Fireball = GameConstants.SkillId_Fireball;
        const int Cleave = GameConstants.SkillId_Cleave;
        const int Charge = GameConstants.SkillId_Charge;
        const int TripleSlash = GameConstants.SkillId_TripleSlash;
        const int PowerStrike = GameConstants.SkillId_PowerStrike;
        const int Execute = GameConstants.SkillId_Execute;

        /// <summary>A 冲撞击飞后三连斩连段，B 起手被打断，还不上手。</summary>
        public static StoryClip Suppress()
        {
            var list = new List<StoryCommand>();
            list.Add(new StoryNarrateCommand(0.00f, "压着打 —— A 不给 B 还手的机会"));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            list.Add(new StoryNarrateCommand(0.35f, "玩家A 冲撞击飞"));
            StoryOps.AddSwingAndHit(list, 0.40f, EReplayActor.A, EReplayActor.B, Charge);

            float bSwing = 0.40f + StoryOps.HitTime(Charge) + 0.55f;
            list.Add(new StoryNarrateCommand(bSwing, "玩家B 想还手"));
            list.Add(new StoryCastStartCommand(bSwing + 0.05f, EReplayActor.B, EReplayActor.A, AA));

            float combo = bSwing + 0.28f;
            list.Add(new StoryNarrateCommand(combo, "三连斩打断还手"));
            list.Add(new StoryAbortCastCommand(combo, EReplayActor.B));
            StoryOps.AddAllHits(list, combo, EReplayActor.A, EReplayActor.B, TripleSlash);

            float end = combo + StoryOps.HitTime(TripleSlash, 2) + 2.2f;
            list.Add(new StoryNarrateCommand(end - 1.4f, "B 还不上手"));
            list.Add(new StoryHoldCommand(end));
            return new StoryClip("压着打", list);
        }

        /// <summary>1v1：B 被压到残，闪现拉开，火球反杀 A。</summary>
        public static StoryClip LowHpComeback()
        {
            var list = new List<StoryCommand>();
            const float startGap = 2.0f;
            const float flashDist = 4.5f;
            float fireGap = startGap + flashDist;

            list.Add(new StoryNarrateCommand(0.00f, "丝血反杀 —— B 被压到残，拉开再收头"));
            list.Add(new StorySetPosCommand(0.00f, EReplayActor.A, -startGap * 0.5f, 0f));
            list.Add(new StorySetPosCommand(0.00f, EReplayActor.B, startGap * 0.5f, 0f));
            list.Add(new StorySetHpCommand(0.00f, EReplayActor.B, 0.35f));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            StoryOps.AddSwingAndHit(list, 0.35f, EReplayActor.A, EReplayActor.B, AA);
            StoryOps.AddSwingAndHit(list, 1.05f, EReplayActor.A, EReplayActor.B, AA);

            float executeAt = 1.80f;
            list.Add(new StoryNarrateCommand(executeAt, "A 起处决，B 往后闪"));
            list.Add(new StoryCastStartCommand(executeAt, EReplayActor.A, EReplayActor.B, Execute));
            list.Add(new StoryCastStartCommand(executeAt + 0.08f, EReplayActor.B, EReplayActor.A, Flash, invertDir: true));

            float fireAt = executeAt + 0.55f;
            list.Add(new StoryNarrateCommand(fireAt, "B 火球反杀"));
            list.Add(new StoryFaceCommand(fireAt, EReplayActor.B, EReplayActor.A));
            list.Add(new StoryCastStartCommand(fireAt, EReplayActor.B, EReplayActor.A, Fireball));
            float impact = fireAt + StoryOps.ProjectileImpactDelay(Fireball, fireGap);
            list.Add(new StoryAttackHitCommand(impact, EReplayActor.B, EReplayActor.A, Fireball, lethal: true));

            list.Add(new StoryNarrateCommand(impact + 0.15f, "A 倒下"));
            list.Add(new StoryHoldCommand(impact + 2.6f));
            return new StoryClip("丝血反杀", list);
        }

        /// <summary>两边都打得上，最后 A 收掉 B。</summary>
        public static StoryClip TradeToKill()
        {
            var list = new List<StoryCommand>();
            list.Add(new StoryNarrateCommand(0.00f, "对拼互爆 —— 两边都能打上"));
            list.Add(new StorySetHpCommand(0.00f, EReplayActor.A, 0.32f));
            list.Add(new StorySetHpCommand(0.00f, EReplayActor.B, 0.32f));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            StoryOps.AddSwingAndHit(list, 0.30f, EReplayActor.A, EReplayActor.B, AA);
            StoryOps.AddSwingAndHit(list, 1.00f, EReplayActor.B, EReplayActor.A, AA);
            StoryOps.AddSwingAndHit(list, 1.70f, EReplayActor.A, EReplayActor.B, AA);
            StoryOps.AddSwingAndHit(list, 2.40f, EReplayActor.B, EReplayActor.A, AA);

            float finisher = 3.20f;
            list.Add(new StoryNarrateCommand(finisher, "A 顺劈收头"));
            StoryOps.AddSwingAndHit(list, finisher, EReplayActor.A, EReplayActor.B, Cleave, lethal: true);

            float end = finisher + StoryOps.HitTime(Cleave) + 2.6f;
            list.Add(new StoryHoldCommand(end));
            return new StoryClip("对拼互爆", list);
        }

        /// <summary>A 重击被躲，B 连打把 A 打残（不打死）。</summary>
        public static StoryClip WhiffPunish()
        {
            var list = new List<StoryCommand>();
            list.Add(new StoryNarrateCommand(0.00f, "空刀反打 —— 打空立刻被罚"));
            list.Add(new StorySetHpCommand(0.00f, EReplayActor.A, 0.42f));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            float strike = 0.35f;
            list.Add(new StoryNarrateCommand(strike, "A 蓄力重击"));
            list.Add(new StoryCastStartCommand(strike, EReplayActor.A, EReplayActor.B, PowerStrike));
            list.Add(new StoryDodgeCommand(strike + 0.70f, EReplayActor.B, enable: true));
            list.Add(new StoryAttackHitCommand(
                strike + StoryOps.HitTime(PowerStrike),
                EReplayActor.A,
                EReplayActor.B,
                PowerStrike));
            list.Add(new StoryDodgeCommand(strike + StoryOps.HitTime(PowerStrike) + 0.08f, EReplayActor.B, enable: false));

            float punish = strike + StoryOps.HitTime(PowerStrike) + 0.20f;
            list.Add(new StoryNarrateCommand(punish, "B 抓住空档连打"));
            StoryOps.AddSwingAndHit(list, punish, EReplayActor.B, EReplayActor.A, AA);
            StoryOps.AddSwingAndHit(list, punish + 0.70f, EReplayActor.B, EReplayActor.A, AA);

            list.Add(new StoryNarrateCommand(punish + 1.50f, "A 被打残，但还站着"));
            list.Add(new StoryHoldCommand(punish + 2.40f));
            return new StoryClip("空刀反打", list);
        }

        /// <summary>近战贴脸，远程拉开再被冲撞接上。两边都活着结束。</summary>
        public static StoryClip Kite()
        {
            var list = new List<StoryCommand>();
            const float gap = 6.0f;
            list.Add(new StoryNarrateCommand(0.00f, "远程风筝 —— 拉开距离再贴脸"));
            list.Add(new StorySetPosCommand(0.00f, EReplayActor.A, -gap * 0.5f, 0f));
            list.Add(new StorySetPosCommand(0.00f, EReplayActor.B, gap * 0.5f, 0f));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            list.Add(new StoryNarrateCommand(0.30f, "A 冲上来"));
            StoryOps.AddSwingAndHit(list, 0.35f, EReplayActor.A, EReplayActor.B, Charge);

            float flashAt = 0.35f + StoryOps.HitTime(Charge) + 0.18f;
            list.Add(new StoryNarrateCommand(flashAt, "B 后撤放火球"));
            list.Add(new StoryCastStartCommand(flashAt, EReplayActor.B, EReplayActor.A, Flash, invertDir: true));

            float fireAt = flashAt + 0.28f;
            list.Add(new StoryFaceCommand(fireAt, EReplayActor.B, EReplayActor.A));
            list.Add(new StoryCastStartCommand(fireAt, EReplayActor.B, EReplayActor.A, Fireball));
            float afterFlashGap = gap - 4f + 4.5f;
            float impact = fireAt + StoryOps.ProjectileImpactDelay(Fireball, afterFlashGap);
            list.Add(new StoryAttackHitCommand(impact, EReplayActor.B, EReplayActor.A, Fireball));

            float charge2 = impact + 0.20f;
            list.Add(new StoryNarrateCommand(charge2, "A 再冲撞接上"));
            StoryOps.AddSwingAndHit(list, charge2, EReplayActor.A, EReplayActor.B, Charge);

            list.Add(new StoryHoldCommand(charge2 + StoryOps.HitTime(Charge) + 1.8f));
            return new StoryClip("远程风筝", list);
        }

        /// <summary>2v1：A、C 把 B 压到残，B 闪现先秒 C，再三连斩收掉 A。</summary>
        public static StoryClip TwoVOneComeback()
        {
            var list = new List<StoryCommand>();
            list.Add(new StoryNarrateCommand(0.00f, "2v1 反杀 —— 被夹的人把两人都收掉"));
            list.Add(new StorySetHpCommand(0.00f, EReplayActor.B, 0.45f));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.A, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.C, EReplayActor.B));
            list.Add(new StoryFaceCommand(0.05f, EReplayActor.B, EReplayActor.A));

            list.Add(new StoryNarrateCommand(0.30f, "A、C 左右压血"));
            StoryOps.AddSwingAndHit(list, 0.35f, EReplayActor.A, EReplayActor.B, AA);
            StoryOps.AddSwingAndHit(list, 0.40f, EReplayActor.C, EReplayActor.B, Cleave);

            float executeAt = 1.20f;
            list.Add(new StoryNarrateCommand(executeAt, "A 起处决，B 闪进 C 身边"));
            list.Add(new StoryCastStartCommand(executeAt, EReplayActor.A, EReplayActor.B, Execute));
            list.Add(new StoryCastStartCommand(executeAt + 0.08f, EReplayActor.B, EReplayActor.C, Flash));

            float killC = executeAt + 0.50f;
            list.Add(new StoryNarrateCommand(killC, "B 先秒 C"));
            list.Add(new StoryFaceCommand(killC, EReplayActor.B, EReplayActor.C));
            StoryOps.AddSwingAndHit(list, killC, EReplayActor.B, EReplayActor.C, Execute, lethal: true);

            float closeA = killC + StoryOps.HitTime(Execute) + 0.35f;
            list.Add(new StoryNarrateCommand(closeA, "转身冲向 A，三连斩收头"));
            list.Add(new StoryFaceCommand(closeA, EReplayActor.B, EReplayActor.A));
            StoryOps.AddSwingAndHit(list, closeA, EReplayActor.B, EReplayActor.A, Charge);

            float killA = closeA + StoryOps.HitTime(Charge) + 0.18f;
            StoryOps.AddAllHits(list, killA, EReplayActor.B, EReplayActor.A, TripleSlash, lastLethal: true);

            float end = killA + StoryOps.HitTime(TripleSlash, 2) + 2.8f;
            list.Add(new StoryNarrateCommand(end - 1.6f, "场上只留残血 B"));
            list.Add(new StoryHoldCommand(end));
            return new StoryClip("2v1反杀", list, actorCount: 3);
        }
    }
    #endregion
}
