/****************************************************
    文件：TargetingTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：索敌策略与索敌服务的 EditMode 测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class TargetingTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private CombatWorld _world;
        private OperatorConfig _config;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Test Combat World").AddComponent<CombatWorld>();
            _config = ScriptableObject.CreateInstance<OperatorConfig>();
            _config.maxHealth = 100f;
            _config.attack = 10f;
            _config.attackIntervalTicks = 30;
            _config.windupTicks = 9;
            _config.attackRange = 10f;
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

            if (_config != null)
            {
                Object.DestroyImmediate(_config);
            }
        }

        private CombatEntity CreateEntity(string entityName, Faction faction, Vector3 position)
        {
            var go = new GameObject(entityName);
            go.transform.position = position;
            _objects.Add(go);

            CombatEntity entity = go.AddComponent<CombatEntity>();
            entity.Initialize(_world, faction, _config);
            return entity;
        }

        [Test]
        public void NearestSelector_PicksClosestCandidate()
        {
            CombatEntity source = CreateEntity("Source", Faction.Ally, Vector3.zero);
            CombatEntity far = CreateEntity("Far", Faction.Enemy, new Vector3(5f, 0f, 0f));
            CombatEntity near = CreateEntity("Near", Faction.Enemy, new Vector3(2f, 0f, 0f));

            var selector = new NearestTargetSelector();
            CombatEntity result = selector.Select(source, new List<CombatEntity> { far, near });

            Assert.That(result, Is.SameAs(near));
        }

        [Test]
        public void TargetingService_IgnoresAlliesAndOutOfRangeTargets()
        {
            CombatEntity source = CreateEntity("Source", Faction.Ally, Vector3.zero);
            CreateEntity("Ally", Faction.Ally, new Vector3(1f, 0f, 0f));
            CreateEntity("FarEnemy", Faction.Enemy, new Vector3(30f, 0f, 0f));
            CombatEntity nearEnemy = CreateEntity("NearEnemy", Faction.Enemy, new Vector3(3f, 0f, 0f));

            CombatEntity result = _world.Targeting.Select(source);

            Assert.That(result, Is.SameAs(nearEnemy));
        }
    }
}
