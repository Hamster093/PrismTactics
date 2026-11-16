/****************************************************
    文件：WaveManager.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 14:45:00
	功能：按波次生成敌人；上一波全部死亡后才开始下一波，支持出怪时机与间隔。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 波次管理器：按 WaveLibrary 的配置节奏生成敌人。
    ///
    /// 核心规则：
    /// - 一波开始后，按每个 spawn 的 spawnTimeSeconds / intervalSeconds 把出怪拆成
    ///   一条条 SpawnEvent，按 tick 升序排列，逐个到点生成；
    /// - 当前波全部生成完，且场上敌人全部死亡/漏掉（LiveEnemies == 0），才允许下一波；
    /// - 下一波的开始时间 = 上一波结束 tick + 该波的 delaySeconds。
    ///
    /// 设计取舍：路线只解析一次并缓存，同一 routeId 的所有敌人共享同一份只读路线，
    /// 避免每个敌人都复制 List。
    /// </summary>
    public sealed class WaveManager
    {
        #region 内部类型

        /// <summary>
        /// 单次出怪事件：在 Tick 时刻、沿 RouteId 路线、生成 EnemyId 敌人。
        /// 一个 WaveSpawnDefinition 的 count 会被展开成多个 SpawnEvent。
        /// </summary>
        private sealed class SpawnEvent
        {
            public int Tick;// 出怪时刻（逻辑 tick 序号）
            public int EnemyId;// 敌人类型 ID
            public string RouteId;// 路线 ID
        }
        #endregion

        #region 字段
        // 波次配置列表（已过滤 null 波次）
        private readonly List<WaveDefinition> _waves = new List<WaveDefinition>();
        // 当前波次的出怪事件列表；StartWave 时重建，按 Tick 升序排列
        private readonly List<SpawnEvent> _activeEvents = new List<SpawnEvent>();
        // 路线缓存：routeId → 从出生点到基地的完整格子路径
        // 路线只解析一次；同一 routeId 的所有敌人共享同一份只读路线，避免每个敌人都复制 List。
        private readonly Dictionary<string, List<Vector2Int>> _routeCache =
            new Dictionary<string, List<Vector2Int>>();
        // 外部依赖：地图（路线/出生点查询）、敌人工厂、战斗总控（添加敌人/查询存活数）
        private readonly BattleMap _map;
        private readonly EnemyFactory _factory;
        private readonly BattleController _controller;
        // 本局总波数、总敌人数（构造时统计）
        private readonly int _totalWaves;
        private readonly int _totalEnemyCount;
        // 下一个待开始的波次索引（0-based）
        private int _nextWaveIndex;
        private int _nextEvent;// 当前波次出怪事件列表中，下一个待生成的事件索引
        private int _nextWaveAllowedTick;// 下一波允许开始的最早 tick（= 上一波结束 tick + delaySeconds）
        private int _currentWave;// 当前波次号（1-based，UI 显示用）
        private int _spawnedWaves; // 已开始的波次数
        private int _spawnedEnemyCount;// 已生成的敌人总数
        private bool _waveInProgress;// 当前是否有一波正在进行中（已开始、未全部生成完或场上还有敌人）
        #endregion

        #region 公开状态属性
        /// <summary>
        /// 所有波次是否已经全部生成完（且没有正在进行的波次）。
        /// 注意：这个属性只看"生成"，不看场上敌人是否清空——清空判断在 BattleController.OnTick 里。
        /// </summary>
        public bool AllWavesSpawned => _nextWaveIndex >= _totalWaves && !_waveInProgress;
        public int TotalWaves => _totalWaves;// 总波数
        public int SpawnedWaves => _spawnedWaves; // 已开始的波次数
        /// <summary>
        /// 当前波次号（1-based）。无波次时返回 0；有波次时夹取到 [1, _totalWaves]，
        /// 避免第一波开始前 UI 显示 0 或越界。
        /// </summary>
        public int CurrentWave => _totalWaves == 0 ? 0 : Mathf.Clamp(_currentWave, 1, _totalWaves);
        public int TotalEnemyCount => _totalEnemyCount;// 本局总敌人数（所有波次 count 之和）
        public int SpawnedEnemyCount => _spawnedEnemyCount;// 已生成的敌人数量
        #endregion

        #region 构造

        /// <summary>
        /// 构造波次管理器：
        /// 1. 过滤 null 波次，统计总波数与总敌人数；
        /// 2. 把第一波的 delaySeconds 换算成 tick，作为首波允许开始时间。
        /// 注意：_totalWaves 先按 library 计数，再用过滤后的 _waves.Count 覆盖，
        /// 保证与实际可执行的波次一致。
        /// </summary>
        public WaveManager(
            WaveLibrary library,
            BattleMap map,
            EnemyFactory factory,
            BattleController controller)
        {
            _map = map;
            _factory = factory;
            _controller = controller;
            _totalWaves = library != null && library.waves != null ? library.waves.Count : 0;

            if (library == null || library.waves == null)
            {
                return;
            }

            for (int i = 0; i < library.waves.Count; i++)
            {
                WaveDefinition wave = library.waves[i];
                if (wave == null)
                {
                    continue;
                }

                _waves.Add(wave);
                if (wave.spawns == null)
                {
                    continue;
                }
                // 累加本波所有 spawn 的敌人数
                for (int s = 0; s < wave.spawns.Count; s++)
                {
                    WaveSpawnDefinition spawn = wave.spawns[s];
                    if (spawn != null && spawn.count > 0)
                    {
                        _totalEnemyCount += spawn.count;
                    }
                }
            }

            _totalWaves = _waves.Count;
            // 首波允许开始时间 = 第一波 delaySeconds 换算的 tick
            _nextWaveAllowedTick = _totalWaves > 0
                ? Mathf.Max(0, Mathf.RoundToInt(_waves[0].delaySeconds * CombatConstants.TickRate))
                : 0;
        }
        #endregion

        #region 主循环 Tick
        /// <summary>
        /// 每逻辑 tick 调用（由 BattleController.OnTick 驱动）：
        /// 1. 若当前无进行中的波次，且到了允许开始时间，则开始下一波；
        /// 2. 把当前波所有到点的 SpawnEvent 逐个生成（一个 tick 可能生成多个）；
        /// 3. 若当前波全部生成完，且场上无存活敌人，则结束本波，计算下一波的允许开始时间。
        /// </summary>
        public void Tick(int tick)
        {
            if (_totalWaves == 0)
            {
                return;
            }
            // 1. 无进行中波次时，尝试开始下一波
            if (!_waveInProgress)
            {
                if (_nextWaveIndex < _totalWaves && tick >= _nextWaveAllowedTick)
                {
                    StartWave(_nextWaveIndex, tick);
                }
                else
                {
                    return;
                }
            }
            // 2. 生成所有到点的出怪事件（按 Tick 升序，所以可以从 _nextEvent 顺序推进）
            while (_nextEvent < _activeEvents.Count
                   && _activeEvents[_nextEvent].Tick <= tick)
            {
                Spawn(_activeEvents[_nextEvent]);
                _nextEvent++;
                _spawnedEnemyCount++;
            }

            // 当前波次全部生成完，并且场上敌人全部死亡/漏掉，才允许下一波。
            if (_nextEvent >= _activeEvents.Count && _controller.LiveEnemies == 0)
            {
                _waveInProgress = false;
                if (_nextWaveIndex < _totalWaves)
                {
                    // 下一波允许开始时间 = 当前 tick + 下一波 delaySeconds
                    int delayTicks = Mathf.Max(
                        0,
                        Mathf.RoundToInt(_waves[_nextWaveIndex].delaySeconds * CombatConstants.TickRate));
                    _nextWaveAllowedTick = tick + delayTicks;
                }
            }
        }
        #endregion

        #region 波次开始

        /// <summary>
        /// 开始指定波次：
        /// 1. 更新波次号/索引/进行中标记；
        /// 2. 清空旧出怪事件；
        /// 3. 把本波每个 spawn 展开成 count 条 SpawnEvent，
        ///    起始 tick = 当前 tick + spawnTimeSeconds，间隔 = intervalSeconds；
        /// 4. 按 Tick 升序排序，供 Tick 循环顺序消费。
        /// </summary>
        private void StartWave(int waveIndex, int tick)
        {
            WaveDefinition wave = _waves[waveIndex];
            _currentWave = waveIndex + 1;
            _spawnedWaves = _currentWave;
            _nextWaveIndex = waveIndex + 1;
            _waveInProgress = true;
            _activeEvents.Clear();
            _nextEvent = 0;

            if (wave.spawns != null)
            {
                for (int s = 0; s < wave.spawns.Count; s++)
                {
                    WaveSpawnDefinition spawn = wave.spawns[s];
                    if (spawn == null || spawn.count <= 0)
                    {
                        continue;
                    }
                    // 第一个敌人的出怪 tick = 当前 tick + spawnTimeSeconds
                    int startTick = tick + Mathf.Max(
                        0,
                        Mathf.RoundToInt(spawn.spawnTimeSeconds * CombatConstants.TickRate));
                    // 相邻敌人间隔，至少 1 tick，避免除零/同 tick 无限堆积
                    int intervalTicks = Mathf.Max(
                        1,
                        Mathf.RoundToInt(spawn.intervalSeconds * CombatConstants.TickRate));
                    // 展开成 count 条事件
                    for (int i = 0; i < spawn.count; i++)
                    {
                        _activeEvents.Add(new SpawnEvent
                        {
                            Tick = startTick + i * intervalTicks,
                            EnemyId = spawn.enemyId,
                            RouteId = spawn.routeId
                        });
                    }
                }
            }
            // 多个 spawn 可能交错时间，统一按 Tick 升序排序
            _activeEvents.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        }
        #endregion

        #region 出怪

        /// <summary>
        /// 生成单个敌人：解析路线 → 工厂创建 → 加入 BattleController。
        /// 路线解析失败时记录错误并跳过本次出怪（不中断后续事件）。
        /// </summary>
        private void Spawn(SpawnEvent spawnEvent)
        {
            List<Vector2Int> route = ResolveRoute(spawnEvent.RouteId);
            if (route == null)
            {
                Debug.LogError("找不到可用路线，波次生成失败：" + spawnEvent.RouteId);
                return;
            }

            EnemyUnit enemy = _factory.Create(spawnEvent.EnemyId, route);
            if (enemy != null)
            {
                _controller.AddEnemy(enemy);
            }
        }
        #endregion

        #region 路线解析

        /// <summary>
        /// 解析 routeId 对应的路线并缓存：
        /// 优先把 routeId 当作"路线 ID"直接查地图配置的完整路线；
        /// 若配置无效（不存在/不完整/起点不是出生点/终点不是基地），则兼容旧配置，
        /// 把 routeId 当作"出生点 ID"做自动寻路。
        /// 两种路径都写入 _routeCache，后续同 routeId 直接返回缓存。
        /// </summary>
        private List<Vector2Int> ResolveRoute(string routeId)
        {
            if (_routeCache.TryGetValue(routeId, out List<Vector2Int> cached))
            {
                return cached;
            }

            // 先按 routeId 找“路线本身”：同一出生点可以配置多条路线。
            IReadOnlyList<Vector2Int> configured = _map.GetRoute(routeId);
            if (configured != null
                && configured.Count > 1
                && GridPathfinder.IsValidRoute(_map, configured)
                && _map.IsSpawnCell(configured[0])
                && configured[configured.Count - 1] == _map.BaseCell)
            {
                var route = new List<Vector2Int>(configured);
                _routeCache[routeId] = route;
                return route;
            }

            // 兼容旧配置：routeId 也等于出生点默认路线时，按出生点做自动寻路。
            SpawnPointDefinition spawnPoint = _map.GetSpawnPoint(routeId);
            if (spawnPoint == null)
            {
                Debug.LogError("地图没有出生点，无法寻路。");
                return null;
            }

            Vector2Int start = spawnPoint.ToPoint().ToVector2Int();
            // 配置了路线但无效时给出警告，便于排查配置问题
            if (configured != null && configured.Count > 0)
            {
                Debug.LogWarning("路线 " + routeId + " 不是从出生点到基地的完整路线，改用自动寻路。");
            }
            // 自动寻路：从出生点找到基地的路径，失败时返回 null（不缓存 null，下次会重试）
            List<Vector2Int> fallback = GridPathfinder.FindPath(_map, start, _map.BaseCell);
            _routeCache[routeId] = fallback;
            return fallback;
        }
        #endregion
    }
}
