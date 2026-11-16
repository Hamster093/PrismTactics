/****************************************************
    文件：BattleMap.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：从 JSON 构建地图，保存地形、可部署、基地、出生点与预设路线。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 战斗地图：把 MapDefinition（JSON 反序列化结果）构建成运行时查询结构。
    ///
    /// 核心机制：
    ///   - 构造时先铺默认地图（全部 Ground + 可部署），再用 definition.cells 覆盖指定格；
    ///   - 基地格和出生点格强制标记为不可部署（防止干员部署到基地/刷怪点上）；
    ///   - 地形、可部署、出生点、路线全部在构造时一次性构建，构造后只读。
    ///
    /// 职责边界：
    /// - 本类只管"地图数据 + 查询"，不管渲染、寻路、碰撞；
    /// - JSON 反序列化由 MapDefinition 负责，本类只消费其结果；
    /// - 格子空间接口 ICombatSpace 的实现在别处，本类只提供 IsInside/GetTerrain 等基础查询。
    ///
    /// 设计取舍：
    /// - definition 为 null 时用硬编码默认地图兜底（MapId="M1_Test"、12×6、基地 (0,3)），
    ///   保证编辑器没配地图也能跑起来；
    /// - 默认所有格子可走可部署，由 cells 显式覆盖"不可走/不可部署"，配置更省事；
    /// - 构造后不可变：没有 setter，没有增删接口，避免运行时地图被意外修改。
    /// </summary>
    public sealed class BattleMap
    {
        #region 公开只读属性
        public string MapId { get; }/// <summary>地图 ID；definition 为 null 时回退硬编码 "M1_Test"。</summary>
        public int Width { get; } /// <summary>地图宽度（格子数）；definition 为 null 时回退硬编码 12，且至少 1。</summary
        public int Height { get; }/// <summary>地图高度（格子数）；definition 为 null 时回退硬编码 6，且至少 1。</summary>
        /// <summary>
        /// 基地格坐标；definition 为 null 时回退硬编码 (0, 3)。
        /// 注意：构造时会尝试将其设为不可部署，但若坐标越界则跳过（见构造函数）。
        /// </summary>
        public Vector2Int BaseCell { get; }
        public float GroundSurfaceY { get; }/// <summary>地面格的世界 Y 高度；definition 为 null 时回退硬编码 0.12f。</summary>
        public float HighGroundSurfaceY { get; }/// <summary>高台格的世界 Y 高度；definition 为 null 时回退硬编码 0.42f。与地面差约 0.3，避免角色悬空。</summary>
        /// <summary>
        /// 出生点列表（只读视图）；由构造时从 definition.spawnPoints 复制而来。
        /// 注意：元素是 SpawnPointDefinition 引用，外部若修改该对象会影响本类
        /// </summary>
        public IReadOnlyList<SpawnPointDefinition> SpawnPoints => _spawnPoints;
        #endregion

        #region 私有状态字段
        /// <summary>
        /// 地形二维数组，索引 [x, y]；构造时默认全部 Ground，由 definition.cells 覆盖。
        /// 越界访问由查询方法兜底（IsInside 检查），数组本身不扩容。
        /// </summary>
        private readonly TerrainType[,] _terrain;
        private readonly bool[,] _deployable;// 可部署标记二维数组，索引 [x, y]；构造时默认全部 true，由 cells 覆盖
        private readonly List<SpawnPointDefinition> _spawnPoints = new List<SpawnPointDefinition>();// 出生点列表；构造时从 definition.spawnPoints 逐个 Add。
        private readonly Dictionary<string, List<Vector2Int>> _routes =
            new Dictionary<string, List<Vector2Int>>();// 路线字典：routeId → 格子坐标列表；构造时从 definition.routes 构建。
        #endregion

        #region 构造与地图构建
        /// <summary>
        /// 从 MapDefinition 构建地图。
        ///
        /// 执行顺序：
        /// 1. 读 MapId/Width/Height/GroundSurfaceY/HighGroundSurfaceY，均有默认值兜底；
        /// 2. 分配 _terrain / _deployable 数组，默认全部 Ground + 可部署；
        /// 3. 若 definition 非 null：
        ///    3.1 遍历 cells，越界跳过，覆盖指定格的地形和可部署；
        ///    3.2 读 BaseCell；
        ///    3.3 复制 spawnPoints（未过滤 null）；
        ///    3.4 遍历 routes，跳过 null/空 id/空 points，构建坐标列表写入 _routes；
        /// 4. 若 definition 为 null：BaseCell 回退 (0, 3)；
        /// 5. 若 BaseCell 在界内，强制设为不可部署；
        /// 6. 遍历所有出生点，在界内的强制设为不可部署。
        ///
        /// 坑点：
        /// - 默认地图是"全 Ground + 全可部署"，配置里只想标"不可走"的格子即可，不用全量配；
        /// - BaseCell 越界时第 5 步跳过，构造不报错，但后续 IsDeployable 里的 `cell == BaseCell` 永远不会匹配；
        /// - 出生点只做"在界内才标记不可部署"，越界出生点不会报错，会安静地留在列表里；
        /// - 路线点没有 IsInside 校验，越界坐标会原样存进 _routes；
        /// - 出生点未过滤 null，后续 IsSpawnCell / GetSpawnPoint 访问 spawn.x 可能 NRE（事实陈述，未改代码）。
        /// </summary>
        public BattleMap(MapDefinition definition)
        {
            // 1.读顶层字段，definition 为 null 时用硬编码默认值兜底。
            MapId = definition != null ? definition.mapId : "M1_Test";
            // 宽高至少 1，避免 0 尺寸数组导致后续越界/除零。
            Width = Mathf.Max(1, definition != null ? definition.width : 12);
            Height = Mathf.Max(1, definition != null ? definition.height : 6);
            // 地表高度默认值：地面 0.12、高台 0.42，两者差 0.3 用于表现高台落差。
            GroundSurfaceY = definition != null ? definition.groundSurfaceY : 0.12f;
            HighGroundSurfaceY = definition != null ? definition.highGroundSurfaceY : 0.42f;

            // 2. 分配二维数组，并铺默认地图：全部 Ground + 可部署。
            _terrain = new TerrainType[Width, Height];
            _deployable = new bool[Width, Height];

            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    // 默认全 Ground 可走、全可部署；cells 只负责覆盖"例外格"。
                    _terrain[x, y] = TerrainType.Ground;
                    _deployable[x, y] = true;
                }
            }

            if (definition != null)
            {
                // 3.1 用 cells 覆盖指定格；越界坐标静默跳过。
                if (definition.cells != null)
                {
                    for (int i = 0; i < definition.cells.Count; i++)
                    {
                        MapCellDefinition cell = definition.cells[i];
                        if (!IsInside(cell.x, cell.y))
                        {
                            continue;
                        }

                        _terrain[cell.x, cell.y] = cell.terrain;
                        _deployable[cell.x, cell.y] = cell.deployable;
                    }
                }
                // 3.2 读基地格；ToVector2Int 的语义由 MapCellDefinition 定义。
                BaseCell = definition.baseCell.ToVector2Int();
                // 3.3 复制出生点；注意未过滤 null 元素。
                if (definition.spawnPoints != null)
                {
                    for (int i = 0; i < definition.spawnPoints.Count; i++)
                    {
                        _spawnPoints.Add(definition.spawnPoints[i]);
                    }
                }
                // 3.4 构建路线；跳过 null / 空 id / 空 points 的非法配置。
                if (definition.routes != null)
                {
                    for (int i = 0; i < definition.routes.Count; i++)
                    {
                        RouteDefinition route = definition.routes[i];
                        if (route == null || string.IsNullOrEmpty(route.id) || route.points == null)
                        {
                            continue;
                        }

                        var cells = new List<Vector2Int>();
                        for (int p = 0; p < route.points.Count; p++)
                        {
                            cells.Add(route.points[p].ToVector2Int());
                        }

                        _routes[route.id] = cells;
                    }
                }
            }
            else
            {
                // 4. definition 为 null 时的默认基地格。
                BaseCell = new Vector2Int(0, 3);
            }
            // 5. 基地格强制不可部署；越界时跳过，不报错。
            if (IsInside(BaseCell.x, BaseCell.y))
            {
                _deployable[BaseCell.x, BaseCell.y] = false;
            }
            // 6. 出生点格强制不可部署；越界出生点跳过，但仍留在 _spawnPoints 列表里。
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                Vector2Int spawn = _spawnPoints[i].ToPoint().ToVector2Int();
                if (IsInside(spawn.x, spawn.y))
                {
                    _deployable[spawn.x, spawn.y] = false;
                }
            }
        }
        #endregion

        #region 地形与可部署查询
        /// <summary>判断格子是否在地图范围内；Vector2Int 重载，转发到 (x, y) 重载。</summary>
        public bool IsInside(Vector2Int cell)
        {
            return IsInside(cell.x, cell.y);
        }
        /// <summary>判断坐标是否在地图范围内；闭区间 [0, Width-1] × [0, Height-1]。</summary>
        public bool IsInside(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }
        /// <summary>
        /// 读取地形类型；越界返回 TerrainType.Void。
        /// 注意：Void 是"越界"的哨兵值，不是地图里配置的地形，配置里写 Void 也会被当成不可走。
        /// </summary>
        public TerrainType GetTerrain(Vector2Int cell)
        {
            return IsInside(cell) ? _terrain[cell.x, cell.y] : TerrainType.Void;
        }
        /// <summary>
        /// 判断格子是否可走：必须在地图内且地形为 Ground。
        /// 注意：HighGround 不可走——高台需要特殊移动逻辑（如跳跃/攀爬），本方法只认地面；
        ///       若未来要让高台可走，需改这里或另开 IsWalkableForHighGround 之类的接口。
        /// </summary>
        public bool IsWalkable(Vector2Int cell)
        {
            return IsInside(cell) && _terrain[cell.x, cell.y] == TerrainType.Ground;
        }
        /// <summary>
        /// 判断格子是否可部署。
        /// 返回 false 的情况：越界、地形为 Void、是基地格、_deployable 标记为 false（cells 配置或出生点/基地强制置 false）。
        /// 注意：`cell == BaseCell` 检查与构造时的强制置 false 有重叠——正常情况下 _deployable 已经是 false，
        ///       这里再查一次是冗余保护；但 BaseCell 越界时构造没置 false，此检查也不会匹配（因为 cell 在界内、BaseCell 在界外），
        ///       实际效果是"越界 BaseCell 不会误伤任何合法格子"（事实陈述，未改代码）。
        /// </summary>
        public bool IsDeployable(Vector2Int cell)
        {
            // 越界或 Void 直接不可部署。
            if (!IsInside(cell) || _terrain[cell.x, cell.y] == TerrainType.Void)
            {
                return false;
            }
            // 基地格不可部署；与构造时的强制置 false 重叠，属冗余保护。
            if (cell == BaseCell)
            {
                return false;
            }

            return _deployable[cell.x, cell.y];
        }
        #endregion

        #region 路线与出生点
        /// <summary>
        /// 按 routeId 取路线格子列表。
        /// 返回 null 的情况：routeId 为空、字典里没有该 id。
        /// 注意：返回的是内部 List 的引用，调用方不要修改；路线点未做 IsInside 校验，可能含越界坐标。
        /// </summary>
        public IReadOnlyList<Vector2Int> GetRoute(string routeId)
        {
            if (string.IsNullOrEmpty(routeId) || !_routes.TryGetValue(routeId, out List<Vector2Int> route))
            {
                return null;
            }

            return route;
        }
        /// <summary>
        /// 按 routeId 找出生点。
        /// 找到第一个 routeId 匹配的出生点返回；找不到时回退到列表第一个；列表为空返回 null。
        /// 注意：多出生点场景下，"找不到匹配 routeId"会回退到第一个出生点，
        ///       可能把敌人刷到错误路线，配置错误时不易察觉（事实陈述，未改代码）。
        /// 注意：_spawnPoints 未过滤 null，若列表里第一个是 null，`_spawnPoints[0]` 返回 null 是安全；
        ///       但循环中 `_spawnPoints[i].routeId` 会 NRE（事实陈述，未改代码）。
        /// </summary>
        public SpawnPointDefinition GetSpawnPoint(string routeId)
        {
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                if (_spawnPoints[i].routeId == routeId)
                {
                    return _spawnPoints[i];
                }
            }

            return _spawnPoints.Count > 0 ? _spawnPoints[0] : null;
        }

        /// <summary>该格是否是任意出生点，编辑器校验自定义路线起点时使用。</summary>
        public bool IsSpawnCell(Vector2Int cell)
        {
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                SpawnPointDefinition spawn = _spawnPoints[i];
                // 显式判空：列表里可能有 null（构造时未过滤）。
                if (spawn != null && spawn.x == cell.x && spawn.y == cell.y)
                {
                    return true;
                }
            }

            return false;
        }
        #endregion

        #region 坐标转换与地表高度
        /// <summary>
        /// 格子坐标转世界坐标，返回 (x, 0, y)——Y 轴固定为 0。
        /// 注意：本方法**不含地表高度**，地面/高台角色都会得到 Y=0；
        ///       需要落地高度时，调用方要额外用 GetSurfaceHeight(cell) 加 Y。
        ///       这是两个方法的职责拆分，不是 bug，但容易误用（事实陈述，未改代码）。
        /// </summary>
        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(cell.x, 0f, cell.y);
        }
        /// <summary>
        /// 世界坐标转格子坐标，对 x/z 四舍五入。
        /// 注意：不校验结果是否在地图内，越界坐标会原样返回；调用方需自行 IsInside。
        /// </summary>
        public Vector2Int WorldToCell(Vector3 world)
        {
            return new Vector2Int(Mathf.RoundToInt(world.x), Mathf.RoundToInt(world.z));
        }

        /// <summary>
        /// 角色脚底所在的地表高度：高台比地面高约 0.3，避免地面角色悬空。
        /// 注意：越界格 GetTerrain 返回 Void，不等于 HighGround，因此返回 GroundSurfaceY 而非报错；
        ///       高台高度差由 GroundSurfaceY / HighGroundSurfaceY 的配置决定（默认 0.12 / 0.42）。
        /// </summary>
        public float GetSurfaceHeight(Vector2Int cell)
        {
            return GetTerrain(cell) == TerrainType.HighGround
                ? HighGroundSurfaceY
                : GroundSurfaceY;
        }
        #endregion
    }
}
