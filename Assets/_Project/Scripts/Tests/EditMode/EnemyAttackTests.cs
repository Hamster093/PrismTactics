/****************************************************
    文件：EnemyAttackTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 23:40:00
	功能：敌人攻击阻挡者、干员死亡释放阻挡并继续漏怪的集成测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class EnemyAttackTests
    {
        private GameObject _worldObject;
        private GameObject _controllerObject;
        private CombatWorld _world;
        private BattleController _controller;
        private EnemyFactory _factory;
        private OperatorConfig _operatorConfig;

        [TearDown]
        public void TearDown()
        {
            if (_factory != null)
            {
                _factory.Dispose();
            }

            if (_controllerObject != null)
            {
                Object.DestroyImmediate(_controllerObject);
            }

            if (_worldObject != null)
            {
                Object.DestroyImmediate(_worldObject);
            }

            if (_operatorConfig != null)
            {
                Object.DestroyImmediate(_operatorConfig);
            }
        }

        [Test]
        public void BlockingEnemy_AttacksAlly_ThenResumesAfterAllyDies()
        {
            MapDefinition mapDefinition = BuildMap();
            var map = new BattleMap(mapDefinition);
            var grid = new BattleGrid(map);

            _worldObject = new GameObject("Enemy Attack World");
            _world = _worldObject.AddComponent<CombatWorld>();
            _world.Space = grid;

            _operatorConfig = ScriptableObject.CreateInstance<OperatorConfig>();
            _operatorConfig.displayName = "测试干员";
            _operatorConfig.maxHealth = 10f;
            _operatorConfig.attack = 0f;
            _operatorConfig.defense = 0f;
            _operatorConfig.blockCount = 2;
            _operatorConfig.deployCost = 0;
            _operatorConfig.rangeCells = new[] { Vector2Int.zero };
            _operatorConfig.normalAttack = null;

            var enemyLibrary = new EnemyLibrary();
            enemyLibrary.enemies.Add(new EnemyDefinition
            {
                id = 1001,
                displayName = "测试敌人",
                maxHealth = 60f,
                attack = 100f,
                attackIntervalTicks = 10,
                windupTicks = 0,
                damageType = 0,
                attackScale = 1f,
                moveSpeed = 1.5f,
                blockWeight = 1,
                lifeDamage = 1
            });

            _controllerObject = new GameObject("Enemy Attack Controller");
            _controller = _controllerObject.AddComponent<BattleController>();
            _controller.Initialize(
                _world,
                map,
                grid,
                new BattleSettings { initialLife = 3, startingCost = 10, costRegenPerSecond = 0f },
                new List<OperatorConfig> { _operatorConfig },
                enemyLibrary,
                new WaveLibrary());

            _factory = new EnemyFactory(_controller, grid, enemyLibrary);
            EnemyUnit enemy = _factory.Create(
                1001,
                new List<Vector2Int>
                {
                    new Vector2Int(2, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(0, 0)
                });
            Assert.That(enemy, Is.Not.Null);
            _controller.AddEnemy(enemy);

            Assert.That(_controller.TryDeploy(_operatorConfig, new Vector2Int(1, 0), GridFacing.Right), Is.True);

            for (int i = 0; i < 400; i++)
            {
                _world.TickOnce();
            }

            Assert.That(_controller.Allies.Count, Is.EqualTo(0));
            Assert.That(_controller.Life, Is.LessThan(3));
        }

        private static MapDefinition BuildMap()
        {
            var definition = new MapDefinition
            {
                mapId = "EnemyAttackTest",
                width = 3,
                height = 1,
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
                        terrain = TerrainType.Ground,
                        deployable = true
                    });
                }
            }

            definition.spawnPoints.Add(new SpawnPointDefinition { x = 2, y = 0, routeId = "test_route" });
            return definition;
        }
    }
}
