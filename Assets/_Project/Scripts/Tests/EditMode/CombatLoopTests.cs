/****************************************************
    文件：CombatLoopTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：完整战斗循环的 EditMode 集成测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class CombatLoopTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private CombatWorld _world;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Loop Combat World").AddComponent<CombatWorld>();
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
        public void TwoUnitsInRange_AcquireTargetsAndDealDamage()
        {
            OperatorConfig allyConfig = CreateConfig("Ally");
            OperatorConfig enemyConfig = CreateConfig("Enemy");

            CombatEntity ally = CreateEntity("Ally", Faction.Ally, Vector3.zero, allyConfig);
            CombatEntity enemy = CreateEntity("Enemy", Faction.Enemy, new Vector3(3f, 0f, 0f), enemyConfig);

            for (int i = 0; i < 120; i++)
            {
                _world.TickOnce();
            }

            Assert.That(ally.CurrentHealth, Is.LessThan(ally.MaxHealth));
            Assert.That(enemy.CurrentHealth, Is.LessThan(enemy.MaxHealth));
        }

        private OperatorConfig CreateConfig(string name)
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            attack.name = name + " Attack";
            attack.damageType = DamageType.Physical;
            attack.attackScale = 1f;
            _assets.Add(attack);

            var config = ScriptableObject.CreateInstance<OperatorConfig>();
            config.name = name + " Config";
            config.maxHealth = 100f;
            config.attack = 10f;
            config.defense = 0f;
            config.attackIntervalTicks = 30;
            config.windupTicks = 9;
            config.attackRange = 5f;
            config.targetSelector = TargetSelectorType.Nearest;
            config.normalAttack = attack;
            _assets.Add(config);
            return config;
        }

        private CombatEntity CreateEntity(string entityName, Faction faction, Vector3 position, OperatorConfig config)
        {
            var go = new GameObject(entityName);
            go.transform.position = position;
            _objects.Add(go);

            CombatEntity entity = go.AddComponent<CombatEntity>();
            entity.Initialize(_world, faction, config);
            return entity;
        }
    }
}
