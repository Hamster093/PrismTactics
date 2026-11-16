/****************************************************
    文件：BattleIntegrationTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：波次敌人沿路线移动到基地并扣除生命值的 EditMode 集成测试。
*****************************************************/

using System.Collections.Generic;
using NUnit.Framework;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class BattleIntegrationTests
    {
        private GameObject _worldObject;
        private GameObject _controllerObject;
        private CombatWorld _world;
        private BattleController _controller;

        [TearDown]
        public void TearDown()
        {
            if (_controllerObject != null)
            {
                Object.DestroyImmediate(_controllerObject);
            }

            if (_worldObject != null)
            {
                Object.DestroyImmediate(_worldObject);
            }
        }

        [Test]
        public void TwoWavesSameEnemy_ReusesPoolAndLeaksBoth()
        {
            MapDefinition mapDefinition = BattleConfigLoader.Load<MapDefinition>(BattleConfigIds.Map);
            EnemyLibrary enemyLibrary = BattleConfigLoader.Load<EnemyLibrary>(BattleConfigIds.Enemies);
            Assert.That(mapDefinition, Is.Not.Null);
            Assert.That(enemyLibrary, Is.Not.Null);

            var map = new BattleMap(mapDefinition);
            var grid = new BattleGrid(map);

            _worldObject = new GameObject("Pool Combat World");
            _world = _worldObject.AddComponent<CombatWorld>();
            _world.Space = grid;

            var settings = new BattleSettings
            {
                initialLife = 3,
                startingCost = 10,
                costRegenPerSecond = 0f,
                maxCost = 99
            };

            var waves = new WaveLibrary();
            for (int i = 0; i < 2; i++)
            {
                var wave = new WaveDefinition { waveNumber = i + 1, delaySeconds = 0f };
                wave.spawns.Add(new WaveSpawnDefinition
                {
                    enemyId = 1,
                    routeId = "route_top",
                    count = 1,
                    intervalSeconds = 0f
                });
                waves.waves.Add(wave);
            }

            _controllerObject = new GameObject("Pool Battle Controller");
            _controller = _controllerObject.AddComponent<BattleController>();
            _controller.Initialize(
                _world,
                map,
                grid,
                settings,
                new List<OperatorConfig>(),
                enemyLibrary,
                waves);

            for (int i = 0; i < 1200; i++)
            {
                _world.TickOnce();
            }

            Assert.That(_controller.TotalEnemies, Is.EqualTo(2));
            Assert.That(_controller.ResolvedEnemies, Is.EqualTo(2));
            Assert.That(_controller.Life, Is.EqualTo(1));
            Assert.That(_controller.Enemies.Count, Is.EqualTo(0));
        }

        [Test]
        public void SingleWaveEnemy_ReachesBaseAndCostsOneLife()
        {
            MapDefinition mapDefinition = BattleConfigLoader.Load<MapDefinition>(BattleConfigIds.Map);
            EnemyLibrary enemyLibrary = BattleConfigLoader.Load<EnemyLibrary>(BattleConfigIds.Enemies);
            Assert.That(mapDefinition, Is.Not.Null);
            Assert.That(enemyLibrary, Is.Not.Null);

            var map = new BattleMap(mapDefinition);
            var grid = new BattleGrid(map);

            _worldObject = new GameObject("Integration Combat World");
            _world = _worldObject.AddComponent<CombatWorld>();
            _world.Space = grid;

            var settings = new BattleSettings
            {
                initialLife = 3,
                startingCost = 10,
                costRegenPerSecond = 0f,
                maxCost = 99
            };

            var waves = new WaveLibrary();
            var wave = new WaveDefinition { waveNumber = 1, delaySeconds = 0f };
            wave.spawns.Add(new WaveSpawnDefinition
            {
                enemyId = 1,
                routeId = "route_top",
                count = 1,
                intervalSeconds = 0f
            });
            waves.waves.Add(wave);

            _controllerObject = new GameObject("Integration Battle Controller");
            _controller = _controllerObject.AddComponent<BattleController>();
            _controller.Initialize(
                _world,
                map,
                grid,
                settings,
                new List<OperatorConfig>(),
                enemyLibrary,
                waves);

            for (int i = 0; i < 600; i++)
            {
                _world.TickOnce();
            }

            Assert.That(_controller.Life, Is.LessThan(3));
            Assert.That(_controller.Enemies.Count, Is.EqualTo(0));
            Assert.That(_controller.TotalEnemies, Is.EqualTo(1));
            Assert.That(_controller.RemainingEnemies, Is.EqualTo(0));
        }
    }
}
