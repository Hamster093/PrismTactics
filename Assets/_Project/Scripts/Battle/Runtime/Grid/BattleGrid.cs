/****************************************************
    文件：BattleGrid.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：战斗格子：地形、单位占用、范围查询与阻挡空间索引。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 战斗格子空间：实现 ICombatSpace，同时充当"部署/阻挡/范围判定"的空间索引。
    ///
    /// 内部维护三张表：
    /// - _allies     ：格子 → 干员（一格最多一个干员，正向索引）；
    /// - _enemies    ：格子 → 敌人列表（一格可堆多个敌人，正向索引）；
    /// - _enemyCells ：敌人 → 它占用的格子列表（反向索引，支持大体型敌人跨多格）。
    ///
    /// 正向索引用于"某格上有什么"，反向索引用于"某实体占了哪些格"，两者必须同步维护。
    /// </summary>
    public sealed class BattleGrid : ICombatSpace
    {
        #region 字段与构造
        // 干员占用表：格子 → 干员（一格只能一个干员）
        private readonly Dictionary<Vector2Int, CombatEntity> _allies =
            new Dictionary<Vector2Int, CombatEntity>();
        // 敌人占用表：格子 → 敌人列表（一格可堆多个敌人）
        // 注意：此表实际存放"非友方实体"，由 Faction 过滤保证只有敌人被查询到
        private readonly Dictionary<Vector2Int, List<CombatEntity>> _enemies =
            new Dictionary<Vector2Int, List<CombatEntity>>();
        // 敌人反向索引：敌人 → 它占用的所有格子（支持一个敌人跨多格，如大体型）
        // 与 _enemies 互为镜像，任何增删必须同时维护两边
        private readonly Dictionary<CombatEntity, List<Vector2Int>> _enemyCells =
            new Dictionary<CombatEntity, List<Vector2Int>>();
        // 临时缓冲：BuildRangeCells 的结果，避免每次范围查询都新建 List
        // 注意：这是共享的临时列表，调用方不要持有其引用，下次查询会被覆盖
        private readonly List<Vector2Int> _rangeCells = new List<Vector2Int>();
        // 地图：提供地形/是否可部署/是否在界内等静态信息
        public BattleMap Map { get; }

        public BattleGrid(BattleMap map)
        {
            Map = map;
        }
        #endregion

        #region 干员格子操作
        /// <summary>
        /// 把干员注册到指定格子。格子越界/干员为空/格子已被占用时返回 false。
        /// 成功后同步更新干员的 GridPosition。
        /// </summary>
        public bool RegisterAlly(Vector2Int cell, CombatEntity ally)
        {
            if (!Map.IsInside(cell) || ally == null || _allies.ContainsKey(cell))
            {
                return false;
            }

            _allies[cell] = ally;
            ally.SetGridPosition(cell, ally.Facing);
            return true;
        }
        /// <summary>
        /// 从指定格子移除干员占用（只删索引，不销毁干员本身）。
        /// </summary>
        public void RemoveAlly(Vector2Int cell)
        {
            _allies.Remove(cell);
        }
        /// <summary>
        /// 查询指定格子上的干员；没有返回 null。
        /// </summary>
        public CombatEntity GetAllyAt(Vector2Int cell)
        {
            return _allies.TryGetValue(cell, out CombatEntity ally) ? ally : null;
        }
        /// <summary>道具可放置格：只按地图的可部署标记判断，不受干员/敌人占位影响。</summary>
        public bool IsItemPlaceable(Vector2Int cell)
        {
            return Map.IsDeployable(cell);
        }
        /// <summary>
        /// 指定格子是否没有任何干员占用（只看干员，不看敌人）。
        /// </summary>
        public bool IsCellFree(Vector2Int cell)
        {
            return !_allies.ContainsKey(cell);
        }
        /// <summary>
        /// 判断指定格子是否可部署该干员：
        /// 1. 地图标记为可部署；
        /// 2. 格子没有干员占用；
        /// 3. 地形符合干员的高台要求。
        /// requiresHighGround 为 true 时自动允许高台，避免"只能高台 + 不允许高台"配置冲突导致无格可放。
        /// </summary>
        public bool IsDeployable(Vector2Int cell, bool canHighGround, bool requiresHighGround = false)
        {
            // 地图层校验：是否在界内、是否标记可部署；再加干员占用校验
            if (!Map.IsDeployable(cell) || !IsCellFree(cell))
            {
                return false;
            }

            TerrainType terrain = Map.GetTerrain(cell);
            if (requiresHighGround)
            {
                // “只能部署高台”本身已经隐含允许高台，避免配置成 canHighGround=false 后无格可放。
                return terrain == TerrainType.HighGround;
            }
            // 普通干员不允许上高台时，高台格直接排除
            if (terrain == TerrainType.HighGround && !canHighGround)
            {
                return false;
            }

            return true;
        }
        #endregion

        #region 敌人格子索引

        /// <summary>
        /// 把敌人注册到指定格子并同步 GridPosition。
        /// 与 AddEnemyToCell 的区别：会更新敌人的逻辑位置。
        /// </summary>
        public void RegisterEnemy(CombatEntity enemy, Vector2Int cell)
        {
            if (enemy == null)
            {
                return;
            }

            AddEnemyToCell(enemy, cell);
            enemy.SetGridPosition(cell, enemy.Facing);
        }
        /// <summary>
        /// 只把敌人加入格子的占用索引（正向 + 反向），不更新 GridPosition。
        /// 供 MoveEnemy 拆分"占格更新"与"位置更新"使用；
        /// 也用于一个敌人同时占用多个格子的场景（对每个格子调用一次）。
        /// </summary>
        public void AddEnemyToCell(CombatEntity enemy, Vector2Int cell)
        {
            if (enemy == null)
            {
                return;
            }
            // 正向：格子 → 敌人列表
            if (!_enemies.TryGetValue(cell, out List<CombatEntity> list))
            {
                list = new List<CombatEntity>();
                _enemies[cell] = list;
            }

            if (!list.Contains(enemy))
            {
                list.Add(enemy);
            }
            // 反向：敌人 → 格子列表
            if (!_enemyCells.TryGetValue(enemy, out List<Vector2Int> cells))
            {
                cells = new List<Vector2Int>();
                _enemyCells[enemy] = cells;
            }

            if (!cells.Contains(cell))
            {
                cells.Add(cell);
            }
        }
        /// <summary>
        /// 把敌人从指定格子的占用索引中移除；正反向都清理，空列表会被删除避免字典膨胀。
        /// </summary>
        public void RemoveEnemyFromCell(CombatEntity enemy, Vector2Int cell)
        {
            if (enemy == null)
            {
                return;
            }
            // 正向清理：从格子的敌人列表中移除，列表空则删掉整个格子键
            if (_enemies.TryGetValue(cell, out List<CombatEntity> list))
            {
                list.Remove(enemy);
                if (list.Count == 0)
                {
                    _enemies.Remove(cell);
                }
            }
            // 反向清理：从敌人的格子列表中移除，列表空则删掉整个敌人键
            if (_enemyCells.TryGetValue(enemy, out List<Vector2Int> cells))
            {
                cells.Remove(cell);
                if (cells.Count == 0)
                {
                    _enemyCells.Remove(enemy);
                }
            }
        }
        /// <summary>
        /// 敌人从 from 格移动到 to 格：先从旧格移除，再注册到新格（会更新 GridPosition）。
        /// 注意这是"一格到一格"的原子移动；多格敌人需分别对每个旧格/新格调用底层方法。
        /// </summary>
        public void MoveEnemy(CombatEntity enemy, Vector2Int from, Vector2Int to)
        {
            if (enemy == null)
            {
                return;
            }

            RemoveEnemyFromCell(enemy, from);
            RegisterEnemy(enemy, to);
        }
        /// <summary>
        /// 从指定格子移除敌人（单格版本，等价于 RemoveEnemyFromCell）。
        /// </summary>
        public void RemoveEnemy(CombatEntity enemy, Vector2Int cell)
        {
            RemoveEnemyFromCell(enemy, cell);
        }
        /// <summary>
        /// 从所有占用格子移除敌人（多格版本）：
        /// 用 snapshot 复制一份格子列表再遍历，因为 RemoveEnemyFromCell 会修改原列表，直接遍历会抛异常。
        /// </summary>
        public void RemoveEnemy(CombatEntity enemy)
        {
            if (enemy == null)
            {
                return;
            }

            if (!_enemyCells.TryGetValue(enemy, out List<Vector2Int> cells))
            {
                return;
            }
            // 快照：避免在遍历中修改 _enemyCells[enemy] 自身
            var snapshot = new List<Vector2Int>(cells);
            for (int i = 0; i < snapshot.Count; i++)
            {
                RemoveEnemyFromCell(enemy, snapshot[i]);
            }
        }
        #endregion

        #region 敌人查询

        /// <summary>
        /// 当前占格敌人数。以反向索引的键数量为准，多格敌人只计一次。
        /// </summary>
        public int EnemyCount => _enemyCells.Count;
        /// <summary>
        /// 获取所有存活敌人（去重：多格敌人只出现一次）。
        /// 遍历反向索引的键，天然去重。
        /// </summary>
        public void GetAllEnemies(List<CombatEntity> results)
        {
            results.Clear();
            foreach (CombatEntity enemy in _enemyCells.Keys)
            {
                if (enemy != null && enemy.IsAlive)
                {
                    results.Add(enemy);
                }
            }
        }
        /// <summary>
        /// 获取指定格子上的所有存活敌人。
        /// 遍历正向索引的单格列表；多格敌人若占用了此格也会被包含。
        /// </summary>
        public void GetEnemiesAt(Vector2Int cell, List<CombatEntity> results)
        {
            results.Clear();
            if (!_enemies.TryGetValue(cell, out List<CombatEntity> list))
            {
                return;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].IsAlive)
                {
                    results.Add(list[i]);
                }
            }
        }
        #endregion

        #region 范围查询

        /// <summary>
        /// 获取 source 攻击范围内的所有敌方目标（无参重载）。
        /// 使用 source.GetEffectiveRange() 取得的范围；空范围直接清空结果。
        /// </summary>
        public void GetTargetsInRange(CombatEntity source, List<CombatEntity> results)
        {
            if (source == null || source.Config == null)
            {
                results.Clear();
                return;
            }

            GetTargetsInRange(source, source.GetEffectiveRange(), results);
        }
        /// <summary>
        /// 获取 source 在指定范围模板下的所有敌方目标：
        /// 1. 用 source.GetEffectiveRange(rangeCells) 得到实际范围（可能被 buff/技能改写）；
        /// 2. 把范围按朝向旋转并裁剪到地图内，写入 _rangeCells；
        /// 3. 遍历范围内所有格子的敌人列表，按阵营过滤并去重。
        /// 过滤同阵营是因为 _enemies 字典实际是"非友方实体"的泛称，不保证只有敌人。
        /// </summary>
        public void GetTargetsInRange(
            CombatEntity source,
            Vector2Int[] rangeCells,
            List<CombatEntity> results)
        {
            results.Clear();
            if (source == null || source.Config == null)
            {
                return;
            }
            // 先让实体决定"实际生效的范围"（可能受 buff 影响）
            Vector2Int[] cells = source.GetEffectiveRange(rangeCells);

            if (cells == null || cells.Length == 0)
            {
                return;
            }
            // 范围偏移按朝向旋转并裁剪，结果写入共享缓冲 _rangeCells
            BuildRangeCells(source.GridPosition, source.Facing, cells);

            for (int i = 0; i < _rangeCells.Count; i++)
            {
                // 跳过空引用、已死亡、同阵营（_enemies 是"非友方"泛称，可能混入己方）
                if (!_enemies.TryGetValue(_rangeCells[i], out List<CombatEntity> list))
                {
                    continue;
                }

                for (int e = 0; e < list.Count; e++)
                {
                    CombatEntity enemy = list[e];
                    if (enemy == null || !enemy.IsAlive || enemy.Faction == source.Faction)
                    {
                        continue;
                    }
                    // 去重：同一敌人可能占用多个格子，被多次命中
                    if (!results.Contains(enemy))
                    {
                        results.Add(enemy);
                    }
                }
            }
        }
        /// <summary>
        /// 获取 source 在指定范围模板下的所有友方目标（治疗/增益类技能用）：
        /// 与 GetTargetsInRange 对称，但查 _allies 表且只保留同阵营。
        /// 注意：rangeCells 为空时回退到 source.Config.rangeCells，不经过 GetEffectiveRange。
        /// </summary>
        public void GetAlliesInRange(
            CombatEntity source,
            Vector2Int[] rangeCells,
            List<CombatEntity> results)
        {
            results.Clear();
            if (source == null || source.Config == null)
            {
                return;
            }
            // 与 GetTargetsInRange 不同：这里不调用 GetEffectiveRange，空则回退到配置范围
            Vector2Int[] cells = rangeCells != null && rangeCells.Length > 0
                ? rangeCells
                : source.Config.rangeCells;

            if (cells == null || cells.Length == 0)
            {
                return;
            }

            BuildRangeCells(source.GridPosition, source.Facing, cells);

            for (int i = 0; i < _rangeCells.Count; i++)
            {
                if (!_allies.TryGetValue(_rangeCells[i], out CombatEntity ally))
                {
                    continue;
                }

                if (ally != null && ally.IsAlive && ally.Faction == source.Faction && !results.Contains(ally))
                {
                    results.Add(ally);
                }
            }
        }
        /// <summary>
        /// 判定 target 是否在 source 的攻击范围内（ICombatSpace 接口实现）：
        /// 1. 用 source.Config.rangeCells 构建范围；
        /// 2. 若 target 是多格敌人，检查它占用的任意格是否落在范围内；
        /// 3. 否则用 target.GridPosition 直接判断。
        /// </summary>
        public bool IsInRange(CombatEntity source, CombatEntity target)
        {
            if (source == null || target == null || source.Config == null || source.Config.rangeCells == null)
            {
                return false;
            }
            // 用干员配置的原始范围（不经过 GetEffectiveRange）构建 _rangeCells
            BuildRangeCells(source.GridPosition, source.Facing, source.Config.rangeCells);
            // 多格敌人：只要任意一个占用格在范围内即视为命中
            if (_enemyCells.TryGetValue(target, out List<Vector2Int> occupied))
            {
                for (int i = 0; i < occupied.Count; i++)
                {
                    if (_rangeCells.Contains(occupied[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
            // 单格目标（含干员）：直接比较所在格
            return _rangeCells.Contains(target.GridPosition);
        }
        #endregion

        #region 范围计算辅助

        /// <summary>
        /// 把范围偏移按朝向旋转。默认 offset 对应"朝右"（GridFacing.Right），
        /// 即范围内每个偏移量都以"朝右"为基准配置，其他朝向通过 90° 整数旋转得到：
        /// - 右 (默认)：offset 不变；
        /// - 上：(x, y) → (-y, x)（逆时针 90°）；
        /// - 左：(x, y) → (-x, -y)（旋转 180°）；
        /// - 下：(x, y) → (y, -x)（顺时针 90°）。
        /// 用整数运算避免浮点误差。
        /// </summary>
        public Vector2Int RotateOffset(Vector2Int offset, GridFacing facing)
        {
            switch (facing)
            {
                case GridFacing.Up:
                    return new Vector2Int(-offset.y, offset.x);
                case GridFacing.Left:
                    return new Vector2Int(-offset.x, -offset.y);
                case GridFacing.Down:
                    return new Vector2Int(offset.y, -offset.x);
                default:
                    return offset;
            }
        }
        /// <summary>
        /// 把"以 origin 为原点的范围偏移"转换为地图上的实际格子列表，写入共享缓冲 _rangeCells。
        /// 每个偏移先按 facing 旋转，再加到 origin 上，最后裁剪掉越界格。
        /// 注意：结果存在 _rangeCells 中，调用方不要持有引用，下次调用会被覆盖。
        /// </summary>
        private void BuildRangeCells(Vector2Int origin, GridFacing facing, Vector2Int[] offsets)
        {
            _rangeCells.Clear();
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector2Int cell = origin + RotateOffset(offsets[i], facing);
                // 裁剪到地图内，避免越界格参与后续查询
                if (Map.IsInside(cell))
                {
                    _rangeCells.Add(cell);
                }
            }
        }
        #endregion
    }
}
