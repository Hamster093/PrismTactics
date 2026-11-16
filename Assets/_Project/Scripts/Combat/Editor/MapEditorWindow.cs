/****************************************************
    文件：MapEditorWindow.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：地图编辑器：绘制地形、设置基地/出生点，并自动或手动编辑敌人路线。
*****************************************************/

using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public sealed class MapEditorWindow : EditorWindow
    {
        private MapDefinition _map;
        private WaveLibrary _waves;
        private BattleSettings _battle;
        private EnemyLibrary _enemies;
        private TerrainType _paintTerrain = TerrainType.Ground;
        private int _selectedX;
        private int _selectedY;
        private string _status = string.Empty;
        private bool _routeEditMode;
        private int _editingSpawnIndex;
        private string _editingRouteId;
        private int _selectedWaveIndex;
        private Vector2 _windowScroll;

        [MenuItem("Tools/PrismTactics/1 内容编辑/1.2 单位与地图/1.2.2 地图编辑器")]
        public static void Open()
        {
            var window = GetWindow<MapEditorWindow>("PrismTactics 地图编辑器");
            window.minSize = new Vector2(620f, 760f);
            window.Show();
        }

        private void OnEnable()
        {
            Load();
        }

        private void OnGUI()
        {
            if (_map == null)
            {
                Load();
            }

            DrawToolbar();
            _windowScroll = EditorGUILayout.BeginScrollView(_windowScroll);
            DrawSettings();
            DrawGrid();
            DrawSelection();
            DrawWaveEditor();
            EditorGUILayout.EndScrollView();
            DrawStatus();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("读取 JSON", EditorStyles.toolbarButton))
            {
                Load();
            }

            if (GUILayout.Button("保存 JSON", EditorStyles.toolbarButton))
            {
                Save();
            }

            if (GUILayout.Button("重新生成所有路线", EditorStyles.toolbarButton))
            {
                GenerateRoutes();
            }

            _routeEditMode = GUILayout.Toggle(_routeEditMode, "编辑路线", EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettings()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("地图设置", EditorStyles.boldLabel);
            _map.mapId = EditorGUILayout.TextField("地图 ID", _map.mapId);
            _map.groundSurfaceY = EditorGUILayout.FloatField("地面高度", _map.groundSurfaceY);
            _map.highGroundSurfaceY = EditorGUILayout.FloatField(
                "高台高度",
                _map.highGroundSurfaceY);
            EditorGUILayout.HelpBox(
                "默认 M1_Test 使用 map.json / waves.json / battle.json；其他 ID 保存为 map_<id>.json 等。" +
                " 进入战斗时在 M1 Battle Bootstrap 的 Map Id 填同一个 ID。",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            _map.width = Mathf.Max(1, EditorGUILayout.IntField("宽度（x）", _map.width));
            _map.height = Mathf.Max(1, EditorGUILayout.IntField("高度（y）", _map.height));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            int baseX = EditorGUILayout.IntField("基地 x", _map.baseCell.x);
            int baseY = EditorGUILayout.IntField("基地 y", _map.baseCell.y);
            _map.baseCell = new GridPoint(baseX, baseY);
            EditorGUILayout.EndHorizontal();

            if (_battle == null)
            {
                _battle = new BattleSettings();
            }

            _battle.startingCost = Mathf.Max(0, EditorGUILayout.IntField(
                "初始费用",
                _battle.startingCost));
            _battle.initialLife = Mathf.Max(1, EditorGUILayout.IntField(
                "初始生命",
                _battle.initialLife));

            EnsureCells();

            if (_routeEditMode)
            {
                DrawRouteSettings();
                return;
            }

            int terrainIndex = GUILayout.Toolbar((int)_paintTerrain, new[] { "地面", "高台", "虚空" });
            _paintTerrain = (TerrainType)terrainIndex;
            EditorGUILayout.HelpBox(
                "左键绘制当前地形；Shift + 左键切换该格是否可部署（地面和高台都有效）。点击格子可选中。",
                MessageType.Info);
        }

        private void DrawRouteSettings()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("路线编辑", EditorStyles.boldLabel);

            if (_map.spawnPoints.Count == 0)
            {
                EditorGUILayout.HelpBox("请先添加出生点，再编辑路线。", MessageType.Warning);
                return;
            }

            _editingSpawnIndex = Mathf.Clamp(_editingSpawnIndex, 0, _map.spawnPoints.Count - 1);
            var spawnLabels = new string[_map.spawnPoints.Count];
            for (int i = 0; i < _map.spawnPoints.Count; i++)
            {
                SpawnPointDefinition spawn = _map.spawnPoints[i];
                spawnLabels[i] = $"({spawn.x},{spawn.y})  {spawn.routeId}";
            }

            _editingSpawnIndex = EditorGUILayout.Popup("编辑出生点", _editingSpawnIndex, spawnLabels);
            SpawnPointDefinition selected = _map.spawnPoints[_editingSpawnIndex];

            string[] routeIds = GetRouteIds();
            if (routeIds.Length == 0)
            {
                RouteDefinition created = CreateRoute(selected, selected.routeId, false);
                _editingRouteId = created.id;
                routeIds = GetRouteIds();
            }

            if (string.IsNullOrEmpty(_editingRouteId) || System.Array.IndexOf(routeIds, _editingRouteId) < 0)
            {
                _editingRouteId = selected.routeId;
            }

            int routeIndex = FindRouteIndex(_editingRouteId);
            int nextRoute = EditorGUILayout.Popup("当前路线", routeIndex, BuildRouteLabels());
            if (nextRoute >= 0 && nextRoute < routeIds.Length)
            {
                _editingRouteId = routeIds[nextRoute];
            }

            RouteDefinition route = GetEditingRoute();
            if (route == null)
            {
                route = CreateRoute(selected, selected.routeId, false);
                _editingRouteId = route.id;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建路线"))
            {
                RouteDefinition created = CreateRoute(selected, MakeUniqueRouteId(selected.routeId), false);
                _editingRouteId = created.id;
                route = created;
            }

            if (GUILayout.Button("复制当前路线"))
            {
                RouteDefinition copied = CreateRoute(selected, MakeUniqueRouteId(route.id), true);
                _editingRouteId = copied.id;
                route = copied;
            }

            if (GUILayout.Button("删除当前路线") && _map.routes.Count > 1)
            {
                string removing = route.id;
                _map.routes.RemoveAll(r => r.id == removing);
                _editingRouteId = selected.routeId;
                route = GetEditingRoute();
                if (route == null)
                {
                    route = CreateRoute(selected, selected.routeId, false);
                    _editingRouteId = route.id;
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("自动寻路到基地"))
            {
                GenerateRouteForRoute(route, selected);
            }

            if (GUILayout.Button("清空路线"))
            {
                route.points.Clear();
                _status = "已清空路线，点击格子从出生点开始追加。";
            }

            if (GUILayout.Button("删除最后一点"))
            {
                if (route.points.Count > 0)
                {
                    route.points.RemoveAt(route.points.Count - 1);
                }
            }

            if (GUILayout.Button("校验路线"))
            {
                _status = ValidateRoute(route);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "按顺序点击相邻格子追加路线。起点必须是出生点，终点必须是基地，不能经过高台/虚空。" +
                $"  当前路线 {route.id}，{route.points.Count} 个点。\n" +
                "同一出生点可以有多条路线：用“新建路线/复制当前路线”创建，再在出怪编辑器里选择。",
                MessageType.Info);
        }

        private void DrawGrid()
        {
            EditorGUILayout.Space(6f);
            for (int y = _map.height - 1; y >= 0; y--)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(y.ToString(), GUILayout.Width(24f));

                for (int x = 0; x < _map.width; x++)
                {
                    MapCellDefinition cell = GetCell(x, y);
                    int routeIndex = _routeEditMode ? GetRouteIndexForEditing(x, y) : -1;
                    bool isRoute = routeIndex >= 0;
                    Color oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = isRoute
                        ? new Color(0.25f, 0.65f, 0.95f)
                        : GetCellColor(cell);

                    string label = IsBase(x, y) ? "基" : IsSpawn(x, y) ? "出" :
                        isRoute ? routeIndex.ToString() :
                        cell.terrain == TerrainType.HighGround
                            ? (cell.deployable ? "高" : "高禁") :
                        cell.terrain == TerrainType.Void ? "空" : "地";

                    if (GUILayout.Button(label, GUILayout.Width(30f), GUILayout.Height(30f)))
                    {
                        _selectedX = x;
                        _selectedY = y;

                        if (_routeEditMode)
                        {
                            AppendRoutePoint(x, y);
                        }
                        else if (Event.current.shift)
                        {
                            cell.deployable = !cell.deployable;
                            _status = $"({x},{y}) 可部署 = {cell.deployable}";
                        }
                        else
                        {
                            cell.terrain = _paintTerrain;
                            if (_paintTerrain == TerrainType.Void)
                            {
                                cell.deployable = false;
                            }
                        }
                    }

                    GUI.backgroundColor = oldColor;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("x →", GUILayout.Width(24f));
            for (int x = 0; x < _map.width; x++)
            {
                GUILayout.Label(x.ToString(), GUILayout.Width(30f));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSelection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField($"选中：({_selectedX}, {_selectedY})", EditorStyles.boldLabel);
            MapCellDefinition selectedCell = GetCell(_selectedX, _selectedY);
            selectedCell.deployable = EditorGUILayout.Toggle(
                "选中格可部署（含高台）",
                selectedCell.deployable);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("设为基地"))
            {
                _map.baseCell = new GridPoint(_selectedX, _selectedY);
            }

            if (GUILayout.Button("添加出生点"))
            {
                AddSpawnPoint(_selectedX, _selectedY);
            }

            if (GUILayout.Button("移除该格出生点"))
            {
                RemoveSpawnPointAtSelected();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("出生点与路线：");
            for (int i = 0; i < _map.spawnPoints.Count; i++)
            {
                SpawnPointDefinition spawn = _map.spawnPoints[i];
                RouteDefinition route = _map.routes.Find(r => r.id == spawn.routeId);
                int pointCount = route != null ? route.points.Count : 0;
                EditorGUILayout.LabelField(
                    $"  ({spawn.x},{spawn.y})  route = {spawn.routeId}  points = {pointCount}");
            }
        }

        private void DrawWaveEditor()
        {
            if (_waves == null)
            {
                _waves = new WaveLibrary();
            }

            if (_waves.waves == null)
            {
                _waves.waves = new List<WaveDefinition>();
            }

            if (_enemies == null)
            {
                _enemies = new EnemyLibrary();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("出怪 / 波次", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "上一波怪物全部死亡/漏掉后，才进入下一波；delay 是下一波开始前等待时间。" +
                " spawnTime 是当前波次开始后的出怪时间。",
                MessageType.Info);

            if (_waves.waves.Count == 0)
            {
                _waves.waves.Add(new WaveDefinition { waveNumber = 1, delaySeconds = 1f });
            }

            var waveLabels = new string[_waves.waves.Count];
            for (int i = 0; i < _waves.waves.Count; i++)
            {
                waveLabels[i] = "波次 " + (i + 1);
            }

            _selectedWaveIndex = Mathf.Clamp(_selectedWaveIndex, 0, _waves.waves.Count - 1);
            EditorGUILayout.BeginHorizontal();
            _selectedWaveIndex = EditorGUILayout.Popup("当前波次", _selectedWaveIndex, waveLabels);
            if (GUILayout.Button("新增波次", GUILayout.Width(80f)))
            {
                _waves.waves.Add(new WaveDefinition
                {
                    waveNumber = _waves.waves.Count + 1,
                    delaySeconds = 1f
                });
                _selectedWaveIndex = _waves.waves.Count - 1;
            }

            if (GUILayout.Button("删除波次", GUILayout.Width(80f)) && _waves.waves.Count > 1)
            {
                _waves.waves.RemoveAt(_selectedWaveIndex);
                _selectedWaveIndex = Mathf.Clamp(
                    _selectedWaveIndex,
                    0,
                    _waves.waves.Count - 1);
            }

            EditorGUILayout.EndHorizontal();

            WaveDefinition wave = _waves.waves[_selectedWaveIndex];
            wave.waveNumber = _selectedWaveIndex + 1;
            wave.delaySeconds = Mathf.Max(
                0f,
                EditorGUILayout.FloatField("开始前等待（秒）", wave.delaySeconds));

            if (wave.spawns == null)
            {
                wave.spawns = new List<WaveSpawnDefinition>();
            }

            for (int i = 0; i < wave.spawns.Count; i++)
            {
                WaveSpawnDefinition spawn = wave.spawns[i];
                if (spawn == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("出怪 " + (i + 1), EditorStyles.boldLabel);
                if (GUILayout.Button("移除", GUILayout.Width(56f)))
                {
                    wave.spawns.RemoveAt(i);
                    i--;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    continue;
                }

                EditorGUILayout.EndHorizontal();

                int enemyIndex = FindEnemyIndex(spawn.enemyId);
                int nextEnemy = EditorGUILayout.Popup(
                    "怪物 ID",
                    enemyIndex,
                    BuildEnemyLabels());
                if (nextEnemy >= 0 && nextEnemy < _enemies.enemies.Count)
                {
                    spawn.enemyId = _enemies.enemies[nextEnemy].id;
                }

                int routeIndex = FindRouteIndex(spawn.routeId);
                int nextRoute = EditorGUILayout.Popup(
                    "出生点 / 路线",
                    routeIndex,
                    BuildRouteLabels());
                string[] routeIds = GetRouteIds();
                if (nextRoute >= 0 && nextRoute < routeIds.Length)
                {
                    spawn.routeId = routeIds[nextRoute];
                }

                spawn.count = Mathf.Max(1, EditorGUILayout.IntField("数量", spawn.count));
                spawn.spawnTimeSeconds = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField("波次开始后时间（秒）", spawn.spawnTimeSeconds));
                spawn.intervalSeconds = Mathf.Max(
                    0f,
                    EditorGUILayout.FloatField("连续出怪间隔（秒）", spawn.intervalSeconds));
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("添加出怪"))
            {
                string[] routeIds = GetRouteIds();
                wave.spawns.Add(new WaveSpawnDefinition
                {
                    enemyId = _enemies.enemies.Count > 0 ? _enemies.enemies[0].id : 1,
                    routeId = routeIds.Length > 0 ? routeIds[0] : "route_default",
                    count = 1,
                    spawnTimeSeconds = 0f,
                    intervalSeconds = 0f
                });
            }
        }

        private int FindEnemyIndex(int enemyId)
        {
            if (_enemies == null || _enemies.enemies == null)
            {
                return 0;
            }

            for (int i = 0; i < _enemies.enemies.Count; i++)
            {
                if (_enemies.enemies[i] != null && _enemies.enemies[i].id == enemyId)
                {
                    return i;
                }
            }

            return 0;
        }

        private string[] BuildEnemyLabels()
        {
            if (_enemies == null || _enemies.enemies == null || _enemies.enemies.Count == 0)
            {
                return new[] { "(没有怪物配置)" };
            }

            var labels = new string[_enemies.enemies.Count];
            for (int i = 0; i < _enemies.enemies.Count; i++)
            {
                EnemyDefinition enemy = _enemies.enemies[i];
                labels[i] = enemy != null ? $"#{enemy.id} {enemy.displayName}" : "(空)";
            }

            return labels;
        }

        private string[] GetRouteIds()
        {
            var ids = new List<string>();

            if (_map != null && _map.routes != null)
            {
                for (int i = 0; i < _map.routes.Count; i++)
                {
                    RouteDefinition route = _map.routes[i];
                    if (route != null && !string.IsNullOrEmpty(route.id) && !ids.Contains(route.id))
                    {
                        ids.Add(route.id);
                    }
                }
            }

            if (_map != null && _map.spawnPoints != null)
            {
                for (int i = 0; i < _map.spawnPoints.Count; i++)
                {
                    string routeId = _map.spawnPoints[i].routeId;
                    if (!string.IsNullOrEmpty(routeId) && !ids.Contains(routeId))
                    {
                        ids.Add(routeId);
                    }
                }
            }

            return ids.ToArray();
        }

        private string[] BuildRouteLabels()
        {
            string[] ids = GetRouteIds();
            if (ids.Length == 0)
            {
                return new[] { "(先添加出生点)" };
            }

            var labels = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                RouteDefinition route = _map.routes.Find(r => r.id == ids[i]);
                if (route != null && route.points != null && route.points.Count > 0)
                {
                    GridPoint start = route.points[0];
                    labels[i] = $"{ids[i]}  起点({start.x},{start.y})";
                }
                else
                {
                    labels[i] = ids[i];
                }
            }

            return labels;
        }

        private int FindRouteIndex(string routeId)
        {
            string[] ids = GetRouteIds();
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == routeId)
                {
                    return i;
                }
            }

            return 0;
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }
        }

        private void Load()
        {
            string requestedMapId = _map != null ? _map.mapId : string.Empty;
            _map = BattleConfigLoader.LoadMap(requestedMapId);
            if (_map == null)
            {
                _map = CreateDefaultMap();
            }

            _waves = BattleConfigLoader.LoadWaves(_map.mapId);
            if (_waves == null)
            {
                _waves = new WaveLibrary();
            }

            _battle = BattleConfigLoader.LoadBattle(_map.mapId);
            if (_battle == null)
            {
                _battle = new BattleSettings();
            }

            _enemies = BattleConfigLoader.Load<EnemyLibrary>(BattleConfigIds.Enemies);
            if (_enemies == null)
            {
                _enemies = new EnemyLibrary();
            }

            NormalizeMap();
            EnsureCells();
            _selectedWaveIndex = Mathf.Clamp(
                _selectedWaveIndex,
                0,
                Mathf.Max(0, _waves.waves.Count - 1));
            _status = "已读取地图 " + _map.mapId + " 及波次/初始费用。";
        }

        private void Save()
        {
            EnsureCells();
            if (_waves == null)
            {
                _waves = new WaveLibrary();
            }

            if (_battle == null)
            {
                _battle = new BattleSettings();
            }

            string mapId = string.IsNullOrEmpty(_map.mapId) ? "M1_Test" : _map.mapId;
            string mapPath = ConfigPath(BattleConfigLoader.GetMapConfigId(mapId));
            string wavesPath = ConfigPath(BattleConfigLoader.GetWavesConfigId(mapId));
            string battlePath = ConfigPath(BattleConfigLoader.GetBattleConfigId(mapId));

            Directory.CreateDirectory(Path.GetDirectoryName(mapPath));
            File.WriteAllText(mapPath, JsonUtility.ToJson(_map, true));
            File.WriteAllText(wavesPath, JsonUtility.ToJson(_waves, true));
            File.WriteAllText(battlePath, JsonUtility.ToJson(_battle, true));
            AssetDatabase.Refresh();
            _status = "已保存地图 " + mapId + "，波次与初始费用一并保存。";
        }

        private static string ConfigPath(string configId)
        {
            return BattleConfigLoader.GetConfigFilePath(configId);
        }

        private void GenerateRoutes()
        {
            EnsureCells();
            var battleMap = new BattleMap(_map);

            for (int i = 0; i < _map.spawnPoints.Count; i++)
            {
                SpawnPointDefinition spawn = _map.spawnPoints[i];
                List<Vector2Int> path = GridPathfinder.FindPath(
                    battleMap,
                    spawn.ToPoint().ToVector2Int(),
                    _map.baseCell.ToVector2Int());

                if (path == null)
                {
                    _status = $"出生点 ({spawn.x},{spawn.y}) 找不到通往基地的路线。";
                    continue;
                }

                RouteDefinition route = _map.routes.Find(r => r.id == spawn.routeId);
                if (route == null)
                {
                    route = new RouteDefinition { id = spawn.routeId };
                    _map.routes.Add(route);
                }

                route.points.Clear();
                for (int p = 0; p < path.Count; p++)
                {
                    route.points.Add(new GridPoint(path[p].x, path[p].y));
                }
            }

            _status = "路线已重新生成，记得保存 JSON。";
        }

        private void GenerateRouteForSpawn(SpawnPointDefinition spawn)
        {
            if (spawn == null)
            {
                return;
            }

            RouteDefinition route = GetRouteForSpawn(spawn);
            GenerateRouteForRoute(route, spawn);
        }

        /// <summary>
        /// 从路线起点（没有点时用出生点）自动寻路到基地；供“自动寻路”按钮和旧入口共用。
        /// </summary>
        private void GenerateRouteForRoute(RouteDefinition route, SpawnPointDefinition spawn)
        {
            if (route == null || spawn == null)
            {
                return;
            }

            EnsureCells();
            var battleMap = new BattleMap(_map);
            Vector2Int start = route.points != null && route.points.Count > 0
                ? route.points[0].ToVector2Int()
                : spawn.ToPoint().ToVector2Int();
            List<Vector2Int> path = GridPathfinder.FindPath(
                battleMap,
                start,
                _map.baseCell.ToVector2Int());

            if (path == null)
            {
                _status = $"路线 {route.id} 从 ({start.x},{start.y}) 找不到通往基地的路径。";
                return;
            }

            route.points.Clear();
            for (int i = 0; i < path.Count; i++)
            {
                route.points.Add(new GridPoint(path[i].x, path[i].y));
            }

            _status = $"已为 {route.id} 生成 {route.points.Count} 个路线点。";
        }

        private void AppendRoutePoint(int x, int y)
        {
            RouteDefinition route = GetEditingRoute();
            if (route == null)
            {
                return;
            }

            var cell = new Vector2Int(x, y);

            if (route.points.Count == 0)
            {
                if (!IsSpawnCell(x, y))
                {
                    _status = "路线必须从出生点开始，请先点击出生点。";
                    return;
                }

                route.points.Add(new GridPoint(x, y));
                _status = "已添加起点，继续点击相邻格子。";
                return;
            }

            Vector2Int last = route.points[route.points.Count - 1].ToVector2Int();
            if (cell == last)
            {
                return;
            }

            if (Mathf.Abs(cell.x - last.x) + Mathf.Abs(cell.y - last.y) != 1)
            {
                _status = "只能点击与最后一个路线点上下左右相邻的格子。";
                return;
            }

            if (last == _map.baseCell.ToVector2Int())
            {
                _status = "路线已经到达基地，不能再追加。";
                return;
            }

            if (!IsRouteWalkable(x, y))
            {
                _status = "敌人不能走高地或虚空，请换一个地面格子。";
                return;
            }

            route.points.Add(new GridPoint(x, y));
            _status = cell == _map.baseCell.ToVector2Int()
                ? "路线已到达基地，记得保存 JSON。"
                : $"已添加到路线 {route.id}：({x},{y})。";
        }

        private string ValidateRoute(RouteDefinition route)
        {
            if (route == null || route.points == null || route.points.Count < 2)
            {
                return "路线至少需要出生点和基地两个点。";
            }

            if (!IsSpawnCell(route.points[0].x, route.points[0].y))
            {
                return "路线起点必须是某个出生点。";
            }

            if (route.points[route.points.Count - 1].ToVector2Int() != _map.baseCell.ToVector2Int())
            {
                return "路线终点必须是基地。";
            }

            for (int i = 0; i < route.points.Count; i++)
            {
                GridPoint point = route.points[i];
                if (!IsRouteWalkable(point.x, point.y))
                {
                    return $"路线点 ({point.x},{point.y}) 在高地或虚空上，敌人无法通过。";
                }

                if (i == 0)
                {
                    continue;
                }

                Vector2Int previous = route.points[i - 1].ToVector2Int();
                Vector2Int current = point.ToVector2Int();
                if (Mathf.Abs(current.x - previous.x) + Mathf.Abs(current.y - previous.y) != 1)
                {
                    return $"路线点 ({point.x},{point.y}) 与前一个点不相邻。";
                }
            }

            return "路线有效。";
        }

        private bool IsSpawnCell(int x, int y)
        {
            for (int i = 0; i < _map.spawnPoints.Count; i++)
            {
                SpawnPointDefinition spawn = _map.spawnPoints[i];
                if (spawn.x == x && spawn.y == y)
                {
                    return true;
                }
            }

            return false;
        }

        private RouteDefinition GetRouteForSpawn(SpawnPointDefinition spawn)
        {
            RouteDefinition route = _map.routes.Find(r => r.id == spawn.routeId);
            if (route == null)
            {
                route = new RouteDefinition { id = spawn.routeId };
                _map.routes.Add(route);
            }

            return route;
        }

        private RouteDefinition GetEditingRoute()
        {
            if (_map == null || _map.routes == null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(_editingRouteId))
            {
                RouteDefinition selected = _map.routes.Find(r => r.id == _editingRouteId);
                if (selected != null)
                {
                    return selected;
                }
            }

            SpawnPointDefinition spawn = GetEditingSpawn();
            return spawn != null ? GetRouteForSpawn(spawn) : null;
        }

        private RouteDefinition GetOrCreateRoute(SpawnPointDefinition spawn)
        {
            return GetEditingRoute() ?? GetRouteForSpawn(spawn);
        }

        private RouteDefinition CreateRoute(SpawnPointDefinition spawn, string routeId, bool copyCurrent)
        {
            string id = string.IsNullOrEmpty(routeId) ? "route_new" : routeId;
            var route = new RouteDefinition { id = id };
            if (copyCurrent)
            {
                RouteDefinition source = GetEditingRoute();
                if (source != null && source.points != null)
                {
                    for (int i = 0; i < source.points.Count; i++)
                    {
                        route.points.Add(new GridPoint(source.points[i].x, source.points[i].y));
                    }
                }
            }
            else if (spawn != null)
            {
                route.points.Add(new GridPoint(spawn.x, spawn.y));
            }

            _map.routes.Add(route);
            return route;
        }

        private string MakeUniqueRouteId(string baseId)
        {
            string id = string.IsNullOrEmpty(baseId) ? "route" : baseId;
            string candidate = id;
            int suffix = 2;
            while (_map.routes.Exists(r => r.id == candidate))
            {
                candidate = id + "_" + suffix;
                suffix++;
            }

            return candidate;
        }

        private SpawnPointDefinition GetEditingSpawn()
        {
            if (_map.spawnPoints.Count == 0)
            {
                return null;
            }

            _editingSpawnIndex = Mathf.Clamp(_editingSpawnIndex, 0, _map.spawnPoints.Count - 1);
            return _map.spawnPoints[_editingSpawnIndex];
        }

        private int GetRouteIndexForEditing(int x, int y)
        {
            RouteDefinition route = GetEditingRoute();
            if (route == null)
            {
                return -1;
            }

            for (int i = 0; i < route.points.Count; i++)
            {
                if (route.points[i].x == x && route.points[i].y == y)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool IsRouteWalkable(int x, int y)
        {
            if (x < 0 || x >= _map.width || y < 0 || y >= _map.height)
            {
                return false;
            }

            return GetCell(x, y).terrain == TerrainType.Ground;
        }

        private void RemoveSpawnPointAtSelected()
        {
            for (int i = _map.spawnPoints.Count - 1; i >= 0; i--)
            {
                SpawnPointDefinition spawn = _map.spawnPoints[i];
                if (spawn.x != _selectedX || spawn.y != _selectedY)
                {
                    continue;
                }

                string routeId = spawn.routeId;
                _map.spawnPoints.RemoveAt(i);

                bool routeStillUsed = _map.spawnPoints.Exists(p => p.routeId == routeId);
                if (!routeStillUsed)
                {
                    _map.routes.RemoveAll(r => r.id == routeId);
                }
            }

            _editingSpawnIndex = Mathf.Clamp(_editingSpawnIndex, 0, Mathf.Max(0, _map.spawnPoints.Count - 1));
            _status = "已移除出生点。";
        }

        private void AddSpawnPoint(int x, int y)
        {
            string routeId = "route_" + x + "_" + y;
            _map.spawnPoints.Add(new SpawnPointDefinition
            {
                x = x,
                y = y,
                routeId = routeId
            });
            _editingSpawnIndex = _map.spawnPoints.Count - 1;
        }

        private void NormalizeMap()
        {
            if (_map.cells == null)
            {
                _map.cells = new List<MapCellDefinition>();
            }

            if (_map.spawnPoints == null)
            {
                _map.spawnPoints = new List<SpawnPointDefinition>();
            }

            if (_map.routes == null)
            {
                _map.routes = new List<RouteDefinition>();
            }

            _map.width = Mathf.Max(1, _map.width);
            _map.height = Mathf.Max(1, _map.height);
        }

        private void EnsureCells()
        {
            if (_map.cells == null)
            {
                _map.cells = new List<MapCellDefinition>();
            }

            var existing = new Dictionary<Vector2Int, MapCellDefinition>();
            for (int i = 0; i < _map.cells.Count; i++)
            {
                MapCellDefinition cell = _map.cells[i];
                existing[new Vector2Int(cell.x, cell.y)] = cell;
            }

            for (int x = 0; x < _map.width; x++)
            {
                for (int y = 0; y < _map.height; y++)
                {
                    var key = new Vector2Int(x, y);
                    if (existing.ContainsKey(key))
                    {
                        continue;
                    }

                    _map.cells.Add(new MapCellDefinition
                    {
                        x = x,
                        y = y,
                        terrain = TerrainType.Ground,
                        deployable = true
                    });
                }
            }
        }

        private MapCellDefinition GetCell(int x, int y)
        {
            for (int i = 0; i < _map.cells.Count; i++)
            {
                MapCellDefinition cell = _map.cells[i];
                if (cell.x == x && cell.y == y)
                {
                    return cell;
                }
            }

            var created = new MapCellDefinition
            {
                x = x,
                y = y,
                terrain = TerrainType.Ground,
                deployable = true
            };
            _map.cells.Add(created);
            return created;
        }

        private bool IsBase(int x, int y)
        {
            return _map.baseCell.x == x && _map.baseCell.y == y;
        }

        private bool IsSpawn(int x, int y)
        {
            for (int i = 0; i < _map.spawnPoints.Count; i++)
            {
                if (_map.spawnPoints[i].x == x && _map.spawnPoints[i].y == y)
                {
                    return true;
                }
            }

            return false;
        }

        private static Color GetCellColor(MapCellDefinition cell)
        {
            switch (cell.terrain)
            {
                case TerrainType.HighGround:
                    return cell.deployable
                        ? new Color(0.45f, 0.65f, 0.72f)
                        : new Color(0.32f, 0.32f, 0.35f);
                case TerrainType.Void:
                    return new Color(0.2f, 0.2f, 0.2f);
                default:
                    return cell.deployable
                        ? new Color(0.45f, 0.75f, 0.55f)
                        : new Color(0.7f, 0.7f, 0.7f);
            }
        }

        private static MapDefinition CreateDefaultMap()
        {
            return new MapDefinition
            {
                mapId = "M1_Test",
                width = 12,
                height = 6,
                baseCell = new GridPoint(0, 3),
                spawnPoints = new List<SpawnPointDefinition>
                {
                    new SpawnPointDefinition { x = 11, y = 1, routeId = "route_top" },
                    new SpawnPointDefinition { x = 11, y = 3, routeId = "route_mid" },
                    new SpawnPointDefinition { x = 11, y = 5, routeId = "route_bottom" }
                }
            };
        }
    }
}
