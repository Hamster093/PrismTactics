/****************************************************
    文件：BattleConfigTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：地图、敌人、干员与波次 JSON 配置的加载测试。
*****************************************************/

using NUnit.Framework;
using PrismTactics.Battle;

namespace PrismTactics.Combat.Tests
{
    public sealed class BattleConfigTests
    {
        [Test]
        public void Map_AllowsMultipleRoutesFromSameSpawn()
        {
            var definition = new MapDefinition
            {
                mapId = "route_test",
                width = 4,
                height = 1,
                baseCell = new GridPoint(3, 0)
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

            definition.spawnPoints.Add(new SpawnPointDefinition { x = 0, y = 0, routeId = "route_a" });

            var routeA = new RouteDefinition { id = "route_a" };
            var routeB = new RouteDefinition { id = "route_b" };
            for (int x = 0; x < 4; x++)
            {
                routeA.points.Add(new GridPoint(x, 0));
                routeB.points.Add(new GridPoint(x, 0));
            }

            definition.routes.Add(routeA);
            definition.routes.Add(routeB);

            var map = new BattleMap(definition);
            UnityEngine.Vector2Int spawn = new UnityEngine.Vector2Int(0, 0);

            Assert.That(map.IsSpawnCell(spawn), Is.True);
            Assert.That(map.GetRoute("route_a"), Is.Not.Null);
            Assert.That(map.GetRoute("route_b"), Is.Not.Null);
            Assert.That(map.GetRoute("route_b")[map.GetRoute("route_b").Count - 1],
                Is.EqualTo(map.BaseCell));
        }

        [Test]
        public void MapConfig_LoadsExpectedGridAndRoutes()
        {
            MapDefinition definition = BattleConfigLoader.Load<MapDefinition>(BattleConfigIds.Map);

            Assert.That(definition, Is.Not.Null);
            Assert.That(definition.width, Is.GreaterThan(0));
            Assert.That(definition.height, Is.GreaterThan(0));
            Assert.That(definition.baseCell.x, Is.InRange(0, definition.width - 1));
            Assert.That(definition.baseCell.y, Is.InRange(0, definition.height - 1));
            Assert.That(definition.spawnPoints.Count, Is.GreaterThan(0));
            Assert.That(definition.routes.Count, Is.GreaterThan(0));

            var map = new BattleMap(definition);
            for (int i = 0; i < definition.routes.Count; i++)
            {
                RouteDefinition route = definition.routes[i];
                var cells = new System.Collections.Generic.List<UnityEngine.Vector2Int>();
                for (int p = 0; p < route.points.Count; p++)
                {
                    cells.Add(route.points[p].ToVector2Int());
                }

                Assert.That(GridPathfinder.IsValidRoute(map, cells), Is.True, route.id);
            }
        }

        [Test]
        public void SkillConfig_HasTenSkillsAndFiveOperators()
        {
            SkillLibrary skills = BattleConfigLoader.Load<SkillLibrary>(BattleConfigIds.Skills);
            OperatorLibrary operators = BattleConfigLoader.Load<OperatorLibrary>(BattleConfigIds.Operators);

            Assert.That(skills, Is.Not.Null);
            Assert.That(operators, Is.Not.Null);
            Assert.That(skills.skills.Count, Is.EqualTo(10));
            Assert.That(operators.operators.Count, Is.EqualTo(5));

            for (int i = 0; i < operators.operators.Count; i++)
            {
                Assert.That(operators.operators[i].skillIds.Count, Is.EqualTo(2));
            }
        }

        [Test]
        public void WaveConfig_HasWavesWithValidSpawns()
        {
            WaveLibrary library = BattleConfigLoader.Load<WaveLibrary>(BattleConfigIds.Waves);

            Assert.That(library, Is.Not.Null);
            Assert.That(library.waves.Count, Is.GreaterThan(0));

            for (int i = 0; i < library.waves.Count; i++)
            {
                Assert.That(library.waves[i].spawns.Count, Is.GreaterThan(0));
                for (int s = 0; s < library.waves[i].spawns.Count; s++)
                {
                    WaveSpawnDefinition spawn = library.waves[i].spawns[s];
                    Assert.That(spawn.enemyId, Is.GreaterThan(0));
                    Assert.That(spawn.count, Is.GreaterThan(0));
                    Assert.That(spawn.spawnTimeSeconds, Is.GreaterThanOrEqualTo(0f));
                }
            }
        }
    }
}
