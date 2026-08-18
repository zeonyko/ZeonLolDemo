using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>开战时读完技能/Buff/弹道/单位/地图表。客户端再多读表现表。</summary>
    public static class BattleCatalogLoader
    {
        /// <summary>读完全部逻辑表。客户端可同时读表现表。</summary>
        public static int LoadCore(bool loadPresentation)
        {
            int skills = LoadSkills(loadPresentation);
            LoadBuffs(loadPresentation);
            LoadProjectiles(loadPresentation);
            LoadUnitProfiles();
            LoadMapCombat();
            ValidateRequiredBuffTemplates();
            WorldCollision.LoadRequiredDefaultArenaFromConfig();
            ValidateUnitAttackSkills();
            return skills;
        }

        static void ValidateRequiredBuffTemplates()
        {
            if (!BuffCatalog.TryGet(GameConstants.BuffId_DotDamage, out _))
            {
                throw new System.IO.InvalidDataException(
                    $"Missing required Buff_{GameConstants.BuffId_DotDamage}.json.");
            }
        }

        static void ValidateUnitAttackSkills()
        {
            foreach (var pair in UnitCatalog.All)
            {
                var profile = pair.Value;
                if (profile == null || profile.AttackSkillId == 0)
                    continue;
                if (SkillCatalog.Get(profile.AttackSkillId) != null)
                    continue;

                throw new System.IO.InvalidDataException(
                    $"UnitProfile entityType={profile.EntityType} references missing skill {profile.AttackSkillId}.");
            }
        }

        static int LoadSkills(bool loadPresentation)
        {
            var logic = new List<SkillConfig>();
            var pres = loadPresentation ? new List<SkillPresentationConfig>() : null;

            foreach (var (path, file) in EachDualKey<SkillConfigFile>("Skills", "Skill_*.json", SkipSkillPath))
            {
                if (file.Logic == null || file.Logic.SkillId <= 0)
                    throw InvalidConfig(path, "Logic.SkillId is required.");
                SkillClipUtil.Normalize(file.Logic);
                logic.Add(file.Logic);
                if (pres == null) continue;

                if (file.Presentation != null)
                    LogicPresentationSplit.NormalizePresentationClips(file.Presentation.Clips);
                else
                {
                    file.Presentation = new SkillPresentationConfig
                    {
                        SkillId = file.Logic.SkillId,
                        Clips = Array.Empty<SkillClip>()
                    };
                }

                pres.Add(file.Presentation);
                Log($"Skill {ConfigPath.FileName(path)} id={file.Logic.SkillId}");
            }

            SkillCatalog.ClearAndLoad(logic);
            if (pres != null)
                SkillPresentationCatalog.ClearAndLoad(pres);
            if (logic.Count == 0)
                throw new System.IO.InvalidDataException("No valid Skill_*.json found.");
            Log($"Skills={logic.Count}");
            return logic.Count;
        }

        static void LoadBuffs(bool loadPresentation)
        {
            var logic = new List<BuffConfig>();
            var pres = loadPresentation ? new List<BuffPresentationConfig>() : null;

            foreach (var (path, file) in EachDualKey<BuffConfigFile>("Buffs", "Buff_*.json"))
            {
                if (file.Logic == null || file.Logic.BuffId == 0)
                    throw InvalidConfig(path, "Logic.BuffId is required.");
                logic.Add(file.Logic);
                if (pres == null) continue;

                pres.Add(file.Presentation ?? new BuffPresentationConfig { BuffId = file.Logic.BuffId });
                Log($"Buff {ConfigPath.FileName(path)} id={file.Logic.BuffId}");
            }

            if (logic.Count == 0)
                throw new System.IO.InvalidDataException("No valid Buff_*.json found.");

            BuffCatalog.ClearAndLoad(logic);
            if (pres != null)
                BuffPresentationCatalog.ClearAndLoad(pres);
        }

        static void LoadProjectiles(bool loadPresentation)
        {
            var logic = new List<ProjectileConfig>();
            var pres = loadPresentation ? new List<ProjectilePresentationConfig>() : null;

            foreach (var (path, file) in EachDualKey<ProjectileConfigFile>("Projectiles", "Projectile_*.json"))
            {
                if (file.Logic == null || file.Logic.ProjectileId == 0)
                    throw InvalidConfig(path, "Logic.ProjectileId is required.");
                logic.Add(file.Logic);
                if (pres == null) continue;

                pres.Add(file.Presentation ?? new ProjectilePresentationConfig
                {
                    ProjectileId = file.Logic.ProjectileId,
                    VfxKey = file.Logic.Name ?? ""
                });
                Log($"Projectile {ConfigPath.FileName(path)} id={file.Logic.ProjectileId}");
            }

            ProjectileCatalog.ClearAndLoad(logic);
            if (pres != null)
                ProjectilePresentationCatalog.ClearAndLoad(pres);
            if (logic.Count == 0)
                throw new System.IO.InvalidDataException("No valid Projectile_*.json found.");
        }

        static void LoadUnitProfiles()
        {
            if (!ConfigService.Exists("Units", "UnitProfiles.json"))
                throw new System.IO.FileNotFoundException("Missing required Units/UnitProfiles.json.");

            var file = ConfigService.LoadFile<UnitProfileConfig>("Units", "UnitProfiles.json");
            if (file?.Profiles == null)
                throw new System.IO.InvalidDataException("Invalid Units/UnitProfiles.json.");

            UnitCatalog.ClearAndLoad(file);
            if (UnitCatalog.All.Count == 0)
                throw new System.IO.InvalidDataException("Units/UnitProfiles.json contains no valid profile.");
            Log($"UnitProfiles loaded={UnitCatalog.All.Count}");
        }

        static void LoadMapCombat()
        {
            if (!ConfigService.Exists("Maps", "Map_HowlingAbyss_Combat.json"))
                throw new System.IO.FileNotFoundException("Missing required Maps/Map_HowlingAbyss_Combat.json.");

            var cfg = ConfigService.LoadFile<MapCombatConfig>("Maps", "Map_HowlingAbyss_Combat.json");
            if (cfg == null)
                throw new System.IO.InvalidDataException("Invalid Maps/Map_HowlingAbyss_Combat.json.");
            if (!string.Equals(cfg.MapId, MapCollisionConfig.DefaultMapId, StringComparison.Ordinal))
            {
                throw new System.IO.InvalidDataException(
                    $"Map combat config id mismatch: {cfg.MapId}.");
            }

            MapCombatCatalog.ClearAndLoad(cfg);
            Log($"MapCombat map={MapCombatCatalog.Current.MapId}");
        }

        static IEnumerable<(string path, TFile file)> EachDualKey<TFile>(
            string relativeDir, string pattern, Func<string, bool> skipPath = null)
            where TFile : class
        {
            foreach (var (path, text) in ConfigService.LoadTexts(relativeDir, pattern))
            {
                if (skipPath != null && skipPath(path)) continue;
                if (!LogicPresentationSplit.IsDualKeyJson(text))
                    throw InvalidConfig(path, "Logic section is required.");

                var file = ConfigService.Json.FromJson<TFile>(text);
                if (file == null)
                    throw InvalidConfig(path, "JSON cannot be read.");
                yield return (path, file);
            }
        }

        static System.IO.InvalidDataException InvalidConfig(string path, string reason) =>
            new System.IO.InvalidDataException($"Invalid config {ConfigPath.FileName(path)}: {reason}");

        static bool SkipSkillPath(string path)
        {
            string n = ConfigPath.Normalize(path);
            return n.IndexOf("/Cast/", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.IndexOf("/Reactions/", StringComparison.OrdinalIgnoreCase) >= 0
                   || n.EndsWith("_Timeline.json", StringComparison.OrdinalIgnoreCase)
                   || n.EndsWith("_Config.json", StringComparison.OrdinalIgnoreCase);
        }

        static void Log(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.Log($"[BattleCatalog] {msg}");
#else
            Console.WriteLine($"[BattleCatalog] {msg}");
#endif
        }

        static void LogError(string msg)
        {
#if UNITY_5_3_OR_NEWER
            UnityEngine.Debug.LogError($"[BattleCatalog] {msg}");
#else
            Console.WriteLine($"[BattleCatalog] ERROR {msg}");
#endif
        }
    }
}
