/****************************************************
    文件：SkillSystemTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 00:30:00
	功能：技能 SP、自动/手动、持续 Buff、治疗目标与瞬间伤害测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class SkillSystemTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private CombatWorld _world;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Skill Test World").AddComponent<CombatWorld>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();

            if (_world != null)
            {
                Object.DestroyImmediate(_world.gameObject);
            }

            for (int i = 0; i < _assets.Count; i++)
            {
                if (_assets[i] != null)
                {
                    Object.DestroyImmediate(_assets[i]);
                }
            }

            _assets.Clear();
        }

        [Test]
        public void AttackRecoverySkill_ChargesAndActivates()
        {
            SkillConfig skill = CreateSkill(
                SkillActivationType.Manual,
                SpRecoveryType.Attack,
                2,
                0,
                SkillEffectType.NextAttack);
            CombatEntity entity = CreateEntity(CreateOperator(false), Vector3.zero, 0, 0);
            SkillComponent component = AttachSkill(entity, skill);

            component.OnAttackPerformed();
            component.OnAttackPerformed();

            Assert.That(component.State, Is.EqualTo(SkillState.Ready));
            Assert.That(component.TryActivate(), Is.True);
            Assert.That(component.CurrentSp, Is.EqualTo(0));
            Assert.That(component.State, Is.EqualTo(SkillState.Charging));
        }

        [Test]
        public void AutoRecoveryTimedBuff_AppliesAndExpires()
        {
            SkillConfig skill = CreateSkill(
                SkillActivationType.Auto,
                SpRecoveryType.Auto,
                2,
                0,
                SkillEffectType.TimedBuff);
            skill.spPerSecond = 30f;
            skill.durationTicks = 2;
            skill.attackPercent = 0.5f;

            CombatEntity entity = CreateEntity(CreateOperator(false), Vector3.zero, 0, 0);
            SkillComponent component = AttachSkill(entity, skill);

            component.Tick();
            component.Tick();

            Assert.That(component.State, Is.EqualTo(SkillState.Active));
            Assert.That(entity.Stats.Get(StatType.Attack), Is.EqualTo(150f).Within(0.001f));

            component.Tick();
            component.Tick();

            Assert.That(component.State, Is.EqualTo(SkillState.Charging));
            Assert.That(entity.Stats.Get(StatType.Attack), Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void Healer_TargetsLowestHealthAlly()
        {
            OperatorConfig healerConfig = CreateOperator(true);
            healerConfig.attackRange = 10f;
            CombatEntity healer = CreateEntity(healerConfig, Vector3.zero, 0, 0);
            CombatEntity fullAlly = CreateEntity(CreateOperator(false), new Vector3(1f, 0f, 0f), 1, 0);
            CombatEntity woundedAlly = CreateEntity(CreateOperator(false), new Vector3(2f, 0f, 0f), 2, 0);

            woundedAlly.Health.TakeDamage(new DamageResult(
                DamageType.True,
                70f,
                70f,
                false,
                false));

            _world.Targeting.ScanAll();

            Assert.That(healer.CurrentTarget, Is.SameAs(woundedAlly));
            Assert.That(fullAlly.CurrentHealth, Is.EqualTo(fullAlly.MaxHealth));
        }

        [Test]
        public void InstantDamageSkill_HitsEnemiesInFront()
        {
            MapDefinition mapDefinition = BuildMap();
            var map = new BattleMap(mapDefinition);
            var grid = new BattleGrid(map);
            _world.Space = grid;

            SkillConfig skill = CreateSkill(
                SkillActivationType.Manual,
                SpRecoveryType.Auto,
                1,
                1,
                SkillEffectType.InstantDamage);
            skill.damageMultiplier = 2f;
            skill.requiresEnemy = true;
            skill.rangeCells = new[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(2, 0),
                new Vector2Int(3, 0)
            };

            CombatEntity ally = CreateEntity(CreateOperator(false), Vector3.zero, 0, 0);
            CombatEntity enemy = CreateEntity(
                CreateEnemy(),
                new Vector3(2f, 0f, 0f),
                2,
                0,
                Faction.Enemy);
            grid.RegisterEnemy(enemy, new Vector2Int(2, 0));
            SkillComponent component = AttachSkill(ally, skill);

            Assert.That(component.TryActivate(), Is.True);
            Assert.That(enemy.CurrentHealth, Is.EqualTo(0f).Within(0.001f));
        }

        private OperatorConfig CreateOperator(bool healer)
        {
            AttackData attack = ScriptableObject.CreateInstance<AttackData>();
            attack.damageType = DamageType.Physical;
            attack.attackScale = 1f;

            var config = ScriptableObject.CreateInstance<OperatorConfig>();
            config.maxHealth = 100f;
            config.attack = 100f;
            config.defense = 0f;
            config.attackIntervalTicks = 30;
            config.windupTicks = 9;
            config.attackRange = 3f;
            config.rangeCells = new[] { Vector2Int.zero };
            config.blockCount = 2;
            config.isHealer = healer;
            config.normalAttack = attack;
            _assets.Add(attack);
            _assets.Add(config);
            return config;
        }

        private EnemyConfig CreateEnemy()
        {
            var config = ScriptableObject.CreateInstance<EnemyConfig>();
            config.maxHealth = 100f;
            config.attack = 0f;
            config.blockCount = 0;
            config.normalAttack = null;
            _assets.Add(config);
            return config;
        }

        private SkillConfig CreateSkill(
            SkillActivationType activation,
            SpRecoveryType recovery,
            int cost,
            int initialSp,
            SkillEffectType effect)
        {
            var skill = ScriptableObject.CreateInstance<SkillConfig>();
            skill.skillId = "test_skill";
            skill.displayName = "测试技能";
            skill.activationType = activation;
            skill.spRecoveryType = recovery;
            skill.spCost = cost;
            skill.initialSp = initialSp;
            skill.spPerSecond = 1f;
            skill.effectType = effect;
            skill.damageMultiplier = 2f;
            skill.hitCount = 1;
            _assets.Add(skill);
            return skill;
        }

        private CombatEntity CreateEntity(
            UnitConfig config,
            Vector3 position,
            int gridX,
            int gridY,
            Faction faction = Faction.Ally)
        {
            var go = new GameObject("Entity");
            go.transform.position = position;
            _objects.Add(go);

            CombatEntity entity = go.AddComponent<CombatEntity>();
            entity.Initialize(_world, faction, config, new Vector2Int(gridX, gridY));
            return entity;
        }

        private SkillComponent AttachSkill(CombatEntity entity, SkillConfig skill)
        {
            SkillComponent component = entity.gameObject.AddComponent<SkillComponent>();
            component.Initialize(entity, new[] { skill });
            entity.AttachSkills(component);
            return component;
        }

        private static MapDefinition BuildMap()
        {
            var definition = new MapDefinition
            {
                mapId = "SkillTest",
                width = 4,
                height = 1,
                baseCell = new GridPoint(0, 0)
            };

            for (int x = 0; x < definition.width; x++)
            {
                definition.cells.Add(new MapCellDefinition
                {
                    x = x,
                    y = 0,
                    terrain = TerrainType.Ground,
                    deployable = true
                });
            }

            definition.spawnPoints.Add(new SpawnPointDefinition { x = 3, y = 0, routeId = "test" });
            return definition;
        }
    }
}
