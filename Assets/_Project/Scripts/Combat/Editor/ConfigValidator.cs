/****************************************************
    文件：ConfigValidator.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 18:30:00
	功能：校验关卡、地图、干员、技能、敌人、波次和藏品配置的引用与命名。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public static class ConfigValidator
    {
        [MenuItem("Tools/PrismTactics/1 内容编辑/1.3 配置校验/1.3.1 一键校验")]
        public static void ValidateAll()
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            LevelLibrary levels = Load<LevelLibrary>("levels", errors);
            OperatorLibrary operators = Load<OperatorLibrary>(BattleConfigIds.Operators, errors);
            SkillLibrary skills = Load<SkillLibrary>(BattleConfigIds.Skills, errors);
            EnemyLibrary enemies = Load<EnemyLibrary>(BattleConfigIds.Enemies, errors);
            RelicLibrary relics = Load<RelicLibrary>("relics", errors);
            ItemLibrary items = Load<ItemLibrary>(BattleConfigIds.Items, errors);

            var operatorIds = new HashSet<string>();
            var skillIds = new HashSet<string>();
            var enemyIds = new HashSet<int>();
            var relicIds = new HashSet<string>();

            if (operators != null && operators.operators != null)
            {
                for (int i = 0; i < operators.operators.Count; i++)
                {
                    OperatorDefinition definition = operators.operators[i];
                    if (definition == null)
                    {
                        errors.Add("operators 第 " + i + " 项为空。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(definition.id))
                    {
                        errors.Add("operators 第 " + i + " 项缺少 id。");
                    }
                    else if (!operatorIds.Add(definition.id))
                    {
                        errors.Add("干员 id 重复：" + definition.id);
                    }

                    if (definition.rangeCells == null || definition.rangeCells.Count == 0)
                    {
                        errors.Add("干员 " + definition.id + " 没有攻击范围。");
                    }

                    if (definition.attackIntervalTicks <= 0)
                    {
                        errors.Add("干员 " + definition.id + " 攻击间隔必须大于 0。");
                    }

                    if (definition.attackHitNormalizedTime < 0f
                        || definition.attackHitNormalizedTime > 1f)
                    {
                        errors.Add("干员 " + definition.id + " 攻击时机必须在 0~1。");
                    }

                    if (definition.useProjectile && definition.projectileSpeed <= 0f)
                    {
                        errors.Add("干员 " + definition.id + " 弹道速度必须大于 0。");
                    }

                    if (definition.skillIds == null || definition.skillIds.Count == 0)
                    {
                        warnings.Add("干员 " + definition.id + " 没有绑定技能。");
                    }
                }
            }

            if (skills != null && skills.skills != null)
            {
                for (int i = 0; i < skills.skills.Count; i++)
                {
                    SkillDefinition skill = skills.skills[i];
                    if (skill == null)
                    {
                        errors.Add("skills 第 " + i + " 项为空。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(skill.id))
                    {
                        errors.Add("skills 第 " + i + " 项缺少 id。");
                    }
                    else if (!skillIds.Add(skill.id))
                    {
                        errors.Add("技能 id 重复：" + skill.id);
                    }

                    if (!Enum.IsDefined(typeof(SkillEffectType), skill.effectType))
                    {
                        errors.Add("技能 " + skill.id + " effectType 非法。");
                    }

                    if (skill.spCost <= 0)
                    {
                        errors.Add("技能 " + skill.id + " spCost 必须大于 0。");
                    }
                }
            }

            if (operators != null && operators.operators != null)
            {
                for (int i = 0; i < operators.operators.Count; i++)
                {
                    OperatorDefinition definition = operators.operators[i];
                    if (definition == null || definition.skillIds == null)
                    {
                        continue;
                    }

                    for (int s = 0; s < definition.skillIds.Count; s++)
                    {
                        if (!skillIds.Contains(definition.skillIds[s]))
                        {
                            errors.Add("干员 " + definition.id + " 引用了不存在的技能：" + definition.skillIds[s]);
                        }
                    }
                }
            }

            if (enemies != null && enemies.enemies != null)
            {
                for (int i = 0; i < enemies.enemies.Count; i++)
                {
                    EnemyDefinition enemy = enemies.enemies[i];
                    if (enemy == null)
                    {
                        errors.Add("enemies 第 " + i + " 项为空。");
                        continue;
                    }

                    if (!enemyIds.Add(enemy.id))
                    {
                        errors.Add("敌人 id 重复：" + enemy.id);
                    }

                    if (enemy.maxHealth <= 0f || enemy.moveSpeed <= 0f)
                    {
                        errors.Add("敌人 " + enemy.id + " 生命/移动速度必须大于 0。");
                    }

                    if (enemy.prewarmCount < 1 || enemy.prewarmCount > 30)
                    {
                        errors.Add("敌人 " + enemy.id + " 预热数量必须在 1~30 之间。");
                    }
                }
            }

            if (relics != null && relics.relics != null)
            {
                for (int i = 0; i < relics.relics.Count; i++)
                {
                    RelicDefinition relic = relics.relics[i];
                    if (relic == null)
                    {
                        errors.Add("relics 第 " + i + " 项为空。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(relic.id))
                    {
                        errors.Add("relics 第 " + i + " 项缺少 id。");
                    }
                    else if (!relicIds.Add(relic.id))
                    {
                        errors.Add("藏品 id 重复：" + relic.id);
                    }

                    if (relic.attackPercent < 0f
                        || relic.healthPercent < 0f
                        || relic.defensePercent < 0f
                        || relic.attackSpeedPercent < 0f)
                    {
                        errors.Add("藏品 " + relic.id + " 存在负加成。");
                    }

                    if (!string.IsNullOrEmpty(relic.icon)
                        && AssetDatabase.LoadAssetAtPath<Sprite>(
                            "Assets/GameContent/" + relic.icon + ".png") == null)
                    {
                        errors.Add("藏品 " + relic.id + " 图标不存在：" + relic.icon);
                    }
                }
            }

            if (items != null && items.items != null)
            {
                var itemIds = new HashSet<string>();
                for (int i = 0; i < items.items.Count; i++)
                {
                    ItemDefinition item = items.items[i];
                    if (item == null)
                    {
                        errors.Add("items 第 " + i + " 项为空。");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        errors.Add("items 第 " + i + " 项缺少 id。");
                    }
                    else if (!itemIds.Add(item.id))
                    {
                        errors.Add("道具 id 重复：" + item.id);
                    }

                    if (string.IsNullOrWhiteSpace(item.effectKey))
                    {
                        errors.Add("道具 " + item.id + " 缺少 effectKey。");
                    }

                    if (item.charges <= 0)
                    {
                        errors.Add("道具 " + item.id + " charges 必须大于 0。");
                    }

                    if (item.radius < 0)
                    {
                        errors.Add("道具 " + item.id + " radius 不能为负。");
                    }
                }
            }

            ValidateLevels(levels, errors, warnings, enemyIds);
            ValidateVisualAssets(errors, warnings);
            Report(errors, warnings);        }

        private static void ValidateLevels(
            LevelLibrary levels,
            List<string> errors,
            List<string> warnings,
            HashSet<int> enemyIds)
        {
            if (levels == null || levels.levels == null || levels.levels.Count == 0)
            {
                errors.Add("levels 没有任何关卡。");
                return;
            }

            var levelIds = new HashSet<string>();
            for (int i = 0; i < levels.levels.Count; i++)
            {
                LevelDefinition level = levels.levels[i];
                if (level == null)
                {
                    errors.Add("levels 第 " + i + " 项为空。");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(level.id) || !levelIds.Add(level.id))
                {
                    errors.Add("关卡 id 为空或重复：" + level.id);
                }

                if (string.IsNullOrWhiteSpace(level.displayName))
                {
                    warnings.Add("关卡 " + level.id + " 没有显示名称。");
                }

                if (string.IsNullOrWhiteSpace(level.mapId))
                {
                    errors.Add("关卡 " + level.id + " 没有 mapId。");
                    continue;
                }

                string mapFile = BattleConfigLoader.GetMapConfigId(level.mapId);
                string wavesFile = BattleConfigLoader.GetWavesConfigId(level.mapId);
                string battleFile = BattleConfigLoader.GetBattleConfigId(level.mapId);
                if (!ConfigExists(mapFile))
                {
                    errors.Add("关卡 " + level.id + " 地图文件不存在：" + mapFile + ".json");
                    continue;
                }

                if (!ConfigExists(wavesFile))
                {
                    warnings.Add("关卡 " + level.id + " 缺少专属波次，会回退 waves.json：" + wavesFile);
                }

                if (!ConfigExists(battleFile))
                {
                    warnings.Add("关卡 " + level.id + " 缺少专属战斗设置，会回退 battle.json：" + battleFile);
                }

                MapDefinition map = Load<MapDefinition>(mapFile, errors);
                if (map != null && !string.Equals(map.mapId, level.mapId, StringComparison.Ordinal))
                {
                    errors.Add("关卡 " + level.id + " 的 mapId 与 " + mapFile + " 内的 mapId 不一致。");
                }

                WaveLibrary waves = Load<WaveLibrary>(wavesFile, errors);
                if (map == null || waves == null || waves.waves == null)
                {
                    continue;
                }

                var routeIds = new HashSet<string>();
                if (map.routes != null)
                {
                    for (int r = 0; r < map.routes.Count; r++)
                    {
                        if (map.routes[r] != null && !string.IsNullOrWhiteSpace(map.routes[r].id))
                        {
                            routeIds.Add(map.routes[r].id);
                        }
                    }
                }

                for (int w = 0; w < waves.waves.Count; w++)
                {
                    WaveDefinition wave = waves.waves[w];
                    if (wave == null || wave.spawns == null)
                    {
                        continue;
                    }

                    for (int s = 0; s < wave.spawns.Count; s++)
                    {
                        WaveSpawnDefinition spawn = wave.spawns[s];
                        if (spawn == null)
                        {
                            continue;
                        }

                        if (!enemyIds.Contains(spawn.enemyId))
                        {
                            errors.Add("关卡 " + level.id + " 波次 " + wave.waveNumber
                                       + " 引用了不存在的敌人 id：" + spawn.enemyId);
                        }

                        if (!string.IsNullOrEmpty(spawn.routeId) && !routeIds.Contains(spawn.routeId))
                        {
                            errors.Add("关卡 " + level.id + " 波次 " + wave.waveNumber
                                       + " 引用了不存在的路线：" + spawn.routeId);
                        }
                    }
                }
            }
        }

        private static void ValidateVisualAssets(List<string> errors, List<string> warnings)
        {
            MapVisualConfig visualConfig = AssetDatabase.LoadAssetAtPath<MapVisualConfig>(
                "Assets/GameContent/MapVisualConfig.asset");
            if (visualConfig == null)
            {
                errors.Add("找不到 GameContent/MapVisualConfig.asset。");
            }
            else
            {
                if (visualConfig.groundCellPrefab == null)
                {
                    errors.Add("MapVisualConfig 缺少 groundCellPrefab。");
                }

                if (visualConfig.highGroundCellPrefab == null)
                {
                    errors.Add("MapVisualConfig 缺少 highGroundCellPrefab。");
                }

                if (visualConfig.baseCellPrefab == null)
                {
                    errors.Add("MapVisualConfig 缺少 baseCellPrefab。");
                }

                if (visualConfig.spawnCellPrefab == null)
                {
                    errors.Add("MapVisualConfig 缺少 spawnCellPrefab。");
                }

                if (visualConfig.backgroundPrefab == null)
                {
                    warnings.Add("MapVisualConfig 没有背景 Prefab。");
                }
            }

            RequireResource<GameObject>("HealthBar", errors);
            RequireResource<GameObject>("DamageNumber", errors);
            RequireResource<GameObject>("Projectile", errors);
        }

        private static void RequireResource<T>(string path, List<string> errors)
            where T : UnityEngine.Object
        {
            string assetPath = "Assets/GameContent/" + path + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) == null)
            {
                errors.Add("缺少 " + assetPath + " 资源。");
            }
        }

        private static T Load<T>(string configId, List<string> errors) where T : class
        {
            string path = BattleConfigLoader.GetConfigFilePath(configId);
            if (!File.Exists(path))
            {
                errors.Add("配置文件不存在：" + path);
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                errors.Add("配置解析失败：" + path + "\n" + exception.Message);
                return null;
            }
        }

        private static bool ConfigExists(string configId)
        {
            return File.Exists(BattleConfigLoader.GetConfigFilePath(configId));
        }

        private static void Report(List<string> errors, List<string> warnings)
        {
            for (int i = 0; i < warnings.Count; i++)
            {
                Debug.LogWarning("[ConfigValidator] " + warnings[i]);
            }

            for (int i = 0; i < errors.Count; i++)
            {
                Debug.LogError("[ConfigValidator] " + errors[i]);
            }

            if (errors.Count == 0)
            {
                Debug.Log("[ConfigValidator] 配置校验通过，警告 " + warnings.Count + " 条。");
            }
            else
            {
                Debug.LogError("[ConfigValidator] 校验失败：错误 " + errors.Count
                               + " 条，警告 " + warnings.Count + " 条。");
            }
        }
    }
}
