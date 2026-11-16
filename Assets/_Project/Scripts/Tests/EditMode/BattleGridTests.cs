/****************************************************
    文件：BattleGridTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：格子地图、寻路高地限制、范围旋转与阻挡数量的 EditMode 测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class BattleGridTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();
        private CombatWorld _world;
        private BattleMap _map;
        private BattleGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _world = new GameObject("Grid Test World").AddComponent<CombatWorld>();
            _map = new BattleMap(BuildMap());
            _grid = new BattleGrid(_map);
            _world.Space = _grid;
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
        public void Pathfinding_AvoidsHighGround()
        {
            List<Vector2Int> path = GridPathfinder.FindPath(_map, new Vector2Int(5, 0), new Vector2Int(0, 0));

            Assert.That(path, Is.Not.Null);
            Assert.That(path, Has.No.Member(new Vector2Int(2, 1)));
        }

        [Test]
        public void RangeQuery_RotatesWithFacing()
        {
            OperatorConfig config = CreateOperatorConfig(1);
            CombatEntity ally = CreateEntity("Ally", Faction.Ally, new Vector2Int(2, 2), config, GridFacing.Right);
            CombatEntity rightTarget = CreateEntity("RightTarget", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);
            CombatEntity upTarget = CreateEntity("UpTarget", Faction.Enemy, new Vector2Int(2, 3), null, GridFacing.Left);
            _grid.RegisterEnemy(rightTarget, new Vector2Int(3, 2));
            _grid.RegisterEnemy(upTarget, new Vector2Int(2, 3));

            Assert.That(_grid.IsInRange(ally, rightTarget), Is.True);
            Assert.That(_grid.IsInRange(ally, upTarget), Is.False);

            ally.SetGridPosition(new Vector2Int(2, 2), GridFacing.Up);

            Assert.That(_grid.IsInRange(ally, rightTarget), Is.False);
            Assert.That(_grid.IsInRange(ally, upTarget), Is.True);
        }

        [Test]
        public void BlockCount_DefaultsToTwoAndRejectsThirdEnemy()
        {
            OperatorConfig config = CreateOperatorConfig(2);
            CombatEntity ally = CreateEntity("Ally", Faction.Ally, new Vector2Int(2, 2), config, GridFacing.Right);
            CombatEntity first = CreateEntity("First", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);
            CombatEntity second = CreateEntity("Second", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);
            CombatEntity third = CreateEntity("Third", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);

            Assert.That(ally.TryBlock(first), Is.True);
            Assert.That(ally.TryBlock(second), Is.True);
            Assert.That(ally.TryBlock(third), Is.False);
            Assert.That(ally.BlockedEnemies.Count, Is.EqualTo(2));

            ally.ReleaseBlock(first);

            Assert.That(ally.TryBlock(third), Is.True);
        }

        [Test]
        public void BlockWeight_ConsumesMultipleSlots()
        {
            OperatorConfig config = CreateOperatorConfig(3);
            CombatEntity ally = CreateEntity("AllyWeight", Faction.Ally, new Vector2Int(2, 2), config, GridFacing.Right);
            CombatEntity heavy = CreateEntity("Heavy", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left, 2);
            CombatEntity normal = CreateEntity("Normal", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);
            CombatEntity extra = CreateEntity("Extra", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);

            Assert.That(ally.TryBlock(heavy), Is.True);
            Assert.That(ally.UsedBlockWeight, Is.EqualTo(2));
            Assert.That(ally.TryBlock(normal), Is.True);
            Assert.That(ally.UsedBlockWeight, Is.EqualTo(3));
            Assert.That(ally.TryBlock(extra), Is.False);

            ally.ReleaseBlock(heavy);
            Assert.That(ally.UsedBlockWeight, Is.EqualTo(1));
            Assert.That(ally.TryBlock(extra), Is.True);
        }

        [Test]
        public void Ally_CanTargetBlockedEnemyOutsideNormalRange()
        {
            OperatorConfig config = CreateOperatorConfig(2);
            CombatEntity ally = CreateEntity("AllyBehind", Faction.Ally, new Vector2Int(2, 2), config, GridFacing.Left);
            CombatEntity enemy = CreateEntity("BehindEnemy", Faction.Enemy, new Vector2Int(3, 2), null, GridFacing.Left);

            Assert.That(ally.TryBlock(enemy), Is.True);
            Assert.That(ally.CurrentTarget, Is.EqualTo(enemy));
            Assert.That(ally.HasValidTarget(), Is.True);

            ally.SetTarget(null);
            _world.Targeting.ScanAll();
            Assert.That(ally.CurrentTarget, Is.EqualTo(enemy));
        }

        [Test]
        public void RequiresHighGround_AllowsHighGroundWhenCanHighGroundIsFalse()
        {
            var high = new Vector2Int(2, 1);
            var ground = new Vector2Int(3, 1);

            Assert.That(_grid.IsDeployable(high, false, true), Is.True);
            Assert.That(_grid.IsDeployable(ground, false, true), Is.False);
        }

        [Test]
        public void Map_BaseAndSpawnCellsAreNotDeployable()
        {
            Assert.That(_map.IsDeployable(_map.BaseCell), Is.False);
            Assert.That(_map.IsDeployable(new Vector2Int(5, 0)), Is.False);
            Assert.That(_map.IsDeployable(new Vector2Int(2, 1)), Is.True);
        }

        private OperatorConfig CreateOperatorConfig(int blockCount)
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            attack.damageType = DamageType.Physical;
            attack.attackScale = 1f;

            var config = ScriptableObject.CreateInstance<OperatorConfig>();
            config.maxHealth = 100f;
            config.attack = 10f;
            config.attackIntervalTicks = 30;
            config.windupTicks = 9;
            config.blockCount = blockCount;
            config.rangeCells = new[] { new Vector2Int(1, 0) };
            config.normalAttack = attack;
            _assets.Add(attack);
            _assets.Add(config);
            return config;
        }

        private CombatEntity CreateEntity(
            string entityName,
            Faction faction,
            Vector2Int cell,
            OperatorConfig config,
            GridFacing facing,
            int blockWeight = 1)
        {
            var go = new GameObject(entityName);
            go.transform.position = new Vector3(cell.x, 0f, cell.y);
            _objects.Add(go);

            CombatEntity entity = go.AddComponent<CombatEntity>();
            if (config == null)
            {
                var enemyConfig = ScriptableObject.CreateInstance<EnemyConfig>();
                enemyConfig.maxHealth = 50f;
                enemyConfig.blockCount = 0;
                enemyConfig.blockWeight = blockWeight;
                enemyConfig.normalAttack = null;
                _assets.Add(enemyConfig);
                entity.Initialize(_world, faction, enemyConfig, cell, facing);
            }
            else
            {
                entity.Initialize(_world, faction, config, cell, facing);
            }

            return entity;
        }

        private static MapDefinition BuildMap()
        {
            var definition = new MapDefinition
            {
                mapId = "GridTest",
                width = 6,
                height = 4,
                baseCell = new GridPoint(0, 0)
            };

            for (int x = 0; x < definition.width; x++)
            {
                for (int y = 0; y < definition.height; y++)
                {
                    definition.cells.Add(new MapCellDefinition
                    {
                        x = x,
                        y = y,
                        terrain = x == 2 && y == 1 ? TerrainType.HighGround : TerrainType.Ground,
                        deployable = true
                    });
                }
            }

            definition.spawnPoints.Add(new SpawnPointDefinition { x = 5, y = 0, routeId = "route_test" });
            return definition;
        }
    }
}
