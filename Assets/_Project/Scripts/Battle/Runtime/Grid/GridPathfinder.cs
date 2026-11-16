/****************************************************
    文件：GridPathfinder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：四方向 A* 寻路，敌人不能走高地与虚空。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 四方向 A* 寻路：在 BattleMap 的格子空间里求 start → goal 的最短路径。
    ///
    /// 核心机制：
    ///   - 邻居固定为右/上/左/下四方向，对应曼哈顿距离启发式（admissible，不高估，保证最短路径）；
    ///   - 可走判据完全委托 BattleMap.IsWalkable，即只有 Ground 可走，HighGround 和 Void 不可走；
    ///   - open 用 List 线性扫描找最小 f 值，closed 用 HashSet，cameFrom 记录前驱，gScore 记录起点距离。
    ///
    /// 职责边界：
    /// - 本类只管"寻路算法"，不关心谁调用、路径拿来干什么；
    /// - 地形可走性、边界判断由 BattleMap 负责，本类不重复实现；
    /// - 路径不做平滑/拉直，返回的是逐格四方向路径。
    ///
    /// 设计取舍：
    /// - 用 List + 线性扫描而非优先队列（二叉堆）：实现简单、无额外依赖，小地图（几十×几十）够用；
    ///   大地图或高频调用时，O(n) 找最小 + O(n) 查 open 会成为瓶颈，届时应换堆 + HashSet；
    /// - 启发式用曼哈顿距离而非欧氏距离：四方向移动下曼哈顿距离不会高估，保证 A* 最优性；
    /// - 静态无状态：不持有任何实例数据，可并发调用（只要 BattleMap 只读）。
    /// </summary>
    public static class GridPathfinder
    {
        #region 常量与字段
        /// <summary>
        /// 四方向邻居偏移：右、上、左、下。
        /// 注意：顺序硬编码，但与算法正确性无关；曼哈顿启发式对应四方向，若改成八方向需同步换启发式。
        /// </summary>
        private static readonly Vector2Int[] Directions =
        {
            Vector2Int.right,
            Vector2Int.up,
            Vector2Int.left,
            Vector2Int.down
        };
        #endregion

        #region 公开接口
        /// <summary>
        /// 求 start → goal 的最短路径（四方向 A*）。
        ///
        /// 执行顺序：
        /// 1. 前置校验：map 非 null、start 和 goal 可走，否则返回 null；
        /// 2. 初始化 open（起点）、closed（空）、cameFrom（空）、gScore（起点 0）；
        /// 3. 循环：从 open 中选 f = g + h 最小的节点；
        ///    3.1 若是 goal，回溯 cameFrom 得到路径并返回；
        ///    3.2 移出 open、加入 closed；
        ///    3.3 遍历四邻居：不可走或已在 closed 则跳过；
        ///    3.4 计算 tentative g，若已有更优 g 则跳过；
        ///    3.5 更新 cameFrom / gScore，不在 open 则加入；
        /// 4. open 耗尽仍无路径，返回 null。
        ///
        /// 前置条件：map 非 null；start / goal 必须在 map 内且可走，否则直接返回 null（不尝试修正）。
        /// 返回：从 start 到 goal 的逐格路径（含两端）；无路径或前置不满足时返回 null。
        /// 注意：start == goal 且可走时，返回只含一个点的路径（循环第一次即命中 goal）。
        /// 注意：本方法无副作用，不修改 map，可重复调用。
        /// </summary>
        public static List<Vector2Int> FindPath(BattleMap map, Vector2Int start, Vector2Int goal)
        {
            if (map == null || !map.IsWalkable(start) || !map.IsWalkable(goal))
            {
                return null;
            }
            // 2. 初始化四个容器：
            //    open   —— 待处理节点（List 线性扫描找最小 f）；
            //    closed —— 已处理节点（HashSet 快速判重）；
            //    cameFrom —— 前驱映射，用于回溯路径；
            //    gScore —— 起点到各节点的已知最短距离。
            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { { start, 0 } };

            while (open.Count > 0)
            {
                // 3. 从 open 中线性扫描 f 最小的节点；f = g + 曼哈顿距离。
                //    性能坑：O(open.Count) 每轮，整体最坏 O(n²)；小地图可接受，大地图应换优先队列。
                //    另注：gScore[open[i]] 直接索引，依赖"加入 open 前一定先写 gScore"的隐式契约。
                int bestIndex = 0;
                int bestScore = int.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    int score = gScore[open[i]] + Manhattan(open[i], goal);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestIndex = i;
                    }
                }

                Vector2Int current = open[bestIndex];
                // 3.1 命中 goal，回溯路径返回。
                if (current == goal)
                {
                    return Reconstruct(cameFrom, current);
                }
                // 3.2 移出 open、加入 closed。
                open.RemoveAt(bestIndex);
                closed.Add(current);
                // 3.3 遍历四方向邻居。
                for (int i = 0; i < Directions.Length; i++)
                {
                    Vector2Int next = current + Directions[i];
                    // 不可走（越界 / 非 Ground / Void）或已处理过，跳过。
                    if (!map.IsWalkable(next) || closed.Contains(next))
                    {
                        continue;
                    }
                    // 3.4 四方向每步代价固定为 1。
                    int tentative = gScore[current] + 1;
                    // 已有更优或相等的 g，无需更新；这里用 >= 而非 >，避免等代价时反复更新前驱。
                    if (gScore.TryGetValue(next, out int known) && tentative >= known)
                    {
                        continue;
                    }
                    // 3.5 更新前驱和 g；不在 open 中则加入。
                    //    性能坑：open.Contains 也是 O(open.Count) 线性扫描，与找最小叠加。
                    cameFrom[next] = current;
                    gScore[next] = tentative;
                    if (!open.Contains(next))
                    {
                        open.Add(next);
                    }
                }
            }
            // 4. open 耗尽仍无路径。
            return null;
        }
        /// <summary>
        /// 校验一条预设路线是否合法。
        ///
        /// 校验规则：
        /// 1. map 和 route 非 null，且点数 >= 2；
        /// 2. 第一个点可走；
        /// 3. 相邻点曼哈顿距离恰好为 1（四方向相邻，不允许斜走/跳格）；
        /// 4. 每个点都可走。
        ///
        /// 返回 false 的情况：map 为空、route 为空、点数 < 2、起点不可走、相邻点非四方向相邻、任一点不可走。
        /// 注意：只校验"逐格连续 + 可走"，不校验"起点是出生点""终点是基地"等业务规则（由调用方负责）。
        /// 注意：硬编码要求四方向相邻，若寻路改为八方向，本方法需同步放宽。
        /// </summary>
        public static bool IsValidRoute(BattleMap map, IReadOnlyList<Vector2Int> route)
        {
            // 1. 基础校验：map / route 非空，且至少两个点（单点路线无意义）。
            if (map == null || route == null || route.Count < 2)
            {
                return false;
            }
            // 2. 起点必须可走。
            if (!map.IsWalkable(route[0]))
            {
                return false;
            }
            // 3. 逐段校验：相邻点曼哈顿距离必须为 1，且每点可走。
            for (int i = 1; i < route.Count; i++)
            {
                Vector2Int delta = route[i] - route[i - 1];
                // 曼哈顿距离 != 1 说明是斜走、跳格或原地重复，均不合法。
                if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
                {
                    return false;
                }

                if (!map.IsWalkable(route[i]))
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 从 cameFrom 回溯路径：从 current 一路找前驱直到起点，再反转。
        /// 前置条件：current 必须可达（通常是从 goal 回溯）；start 没有前驱，循环自然终止。
        /// 返回的列表含起点和终点，顺序为 start → goal。
        /// </summary>
        private static List<Vector2Int> Reconstruct(
            Dictionary<Vector2Int, Vector2Int> cameFrom,
            Vector2Int current)
        {
            var path = new List<Vector2Int> { current };
            // 从终点一路回溯到起点；起点不在 cameFrom 中，TryGetValue 返回 false 时终止。
            while (cameFrom.TryGetValue(current, out Vector2Int previous))
            {
                current = previous;
                path.Add(current);
            }
            // 回溯得到的是 goal → start，反转成 start → goal。
            path.Reverse();
            return path;
        }
        /// <summary>
        /// 曼哈顿距离：|dx| + |dy|。
        /// 用途：四方向移动下作为 A* 启发式，admissible（不高估），保证找到最短路径。
        /// 注意：若改为八方向移动，应换用切比雪夫距离或欧氏距离，否则启发式会高估、A* 不再保证最优。
        /// </summary>
        private static int Manhattan(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
        #endregion
    }
}
