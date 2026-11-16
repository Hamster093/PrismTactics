/****************************************************
    文件：PrefabMapView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 13:00:00
	功能：用配置的格子 Prefab 拼装地图表现，并按 terrain 分层。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    public static class PrefabMapView
    {
        public static void Build(BattleMap map, Transform parent, MapVisualConfig config)
        {
            if (map == null || parent == null || config == null)
            {
                Debug.LogError("PrefabMapView 缺少地图/父节点/表现配置。");
                return;
            }

            var root = new GameObject("Map View").transform;
            root.SetParent(parent, false);

            Transform backgroundLayer = CreateLayer(root, "Background");
            Transform groundLayer = CreateLayer(root, "Ground");
            Transform highGroundLayer = CreateLayer(root, "HighGround");
            Transform decorationLayer = CreateLayer(root, "Decoration");

            var spawnCells = new HashSet<Vector2Int>();
            for (int i = 0; i < map.SpawnPoints.Count; i++)
            {
                spawnCells.Add(map.SpawnPoints[i].ToPoint().ToVector2Int());
            }

            for (int x = 0; x < map.Width; x++)
            {
                for (int y = 0; y < map.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    TerrainType terrain = map.GetTerrain(cell);
                    if (terrain == TerrainType.Void)
                    {
                        continue;
                    }

                    bool isBase = cell == map.BaseCell;
                    bool isSpawn = spawnCells.Contains(cell);
                    bool isHigh = terrain == TerrainType.HighGround;

                    GameObject prefab = isBase
                        ? config.baseCellPrefab
                        : isSpawn
                            ? config.spawnCellPrefab
                            : isHigh
                                ? config.highGroundCellPrefab
                                : config.groundCellPrefab;

                    if (prefab == null)
                    {
                        continue;
                    }

                    Transform layer = isBase || isSpawn
                        ? groundLayer
                        : isHigh
                            ? highGroundLayer
                            : groundLayer;
                 
                    GameObject cellObject = Object.Instantiate(prefab, layer, false);
                    cellObject.name = "Cell_" + x + "_" + y;
                    cellObject.transform.position = map.CellToWorld(cell)
                                                    + Vector3.up * map.GetSurfaceHeight(cell);
                 
                }
            }

            if (config.backgroundPrefab != null)
            {
                GameObject background = Object.Instantiate(config.backgroundPrefab, backgroundLayer, false);
                background.name = "MapBackground";
                background.transform.position = new Vector3(
                    (map.Width - 1) * 0.5f,
                    -0.03f,
                    (map.Height - 1) * 0.5f);
                background.transform.localScale = new Vector3(map.Width, 1f, map.Height);
            }

            // 预留装饰层，后续美术可直接挂装饰 Prefab。
            decorationLayer.gameObject.SetActive(true);
        }

        private static Transform CreateLayer(Transform root, string layerName)
        {
            var layer = new GameObject(layerName).transform;
            layer.SetParent(root, false);
            return layer;
        }
    }
}
