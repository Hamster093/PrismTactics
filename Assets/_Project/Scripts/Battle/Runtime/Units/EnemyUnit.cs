/****************************************************
    文件：EnemyUnit.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 15:45:00
	功能：敌人单位：沿路线移动、进入前阻挡、双格占位、接触攻击与事件驱动解除阻挡。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 敌人单位：沿预设路线逐格移动，被干员阻挡时停下并攻击阻挡者。
    ///
    /// 核心机制：
    /// - 移动：每 tick 朝下一格中心移动 moveSpeed * TickSeconds 距离；
    /// - 阻挡：进入下一格"之前"检查该格是否有干员，有则停下；若干员在敌人已跨过半个
    ///   格子后才部署，每 tick 补一次检查；
    /// - 双格占位：跨过格子边界（距当前格中心 >= 0.5）后，同时注册当前格与下一格，
    ///   供两边范围的干员攻击到它；
    /// - 漏怪/死亡：走到基地格触发 Leak，血量归零触发 HandleDeath，两者都先解除阻挡。
    ///
    /// 阻挡关系的解除是事件驱动的：干员撤退/死亡时触发 BlockReleased 事件，
    /// 敌人订阅该事件，收到后清空目标并解除 _blocked 标记。
    /// </summary>
    public sealed class EnemyUnit : MonoBehaviour
    {
        #region 字段

        // 外部依赖：战斗总控（漏怪/死亡上报）、网格（占格注册/查询）
        private BattleController _controller;
        private BattleGrid _grid;
        // 战斗实体与运行时配置
        private CombatEntity _entity;
        private EnemyConfig _config;
        // 移动路线与当前所在路线索引
        private List<Vector2Int> _route;
        private int _routeIndex;
        // 当前格与下一格
        private Vector2Int _cell;
        private Vector2Int _nextCell;
        // 是否已经确定下一格（避免每 tick 重复计算 _nextCell）
        private bool _hasNextCell;
        // 是否已跨过格子边界、同时占用了下一格（双格占位标记）
        private bool _occupiesNextCell;
        // 是否被阻挡，以及阻挡者引用
        private bool _blocked;
        private CombatEntity _blockedBy;
        // 当前占用的所有格子（通常 1 格，跨格期间为 2 格）
        // 用于 ReleaseForPool / 多格清理时知道自己占了哪些格
        private readonly List<Vector2Int> _occupiedCells = new List<Vector2Int>();
        #endregion

        #region 公开属性

        // 战斗实体
        public CombatEntity Entity => _entity;
        public EnemyConfig Config => _config;// 运行时配置
        public int EnemyId => _config != null ? _config.enemyId : 0;// 敌人类型 ID（配置缺失时返回 0）
        public Vector2Int Cell => _cell;// 当前所在格（逻辑位置，跨格期间仍是"出发格"，直到完全到达下一格才更新）
        public IReadOnlyList<Vector2Int> OccupiedCells => _occupiedCells;// 当前占用的所有格子（跨格期间为 2 格）
        public bool IsBlocked => _blocked;// 是否被阻挡
        #endregion

        #region 初始化与回收

        /// <summary>
        /// 初始化敌人：
        /// 1. 先解除旧的阻挡订阅与死亡订阅（池复用时必须做，避免重复订阅）；
        /// 2. 重置所有移动状态；
        /// 3. 校验路线（至少 2 个点，否则销毁自身）；
        /// 4. 设置初始位置/朝向，初始化 CombatEntity，关闭自动索敌；
        /// 5. 订阅死亡事件，注册到网格。
        /// </summary>
        public void Initialize(
            BattleController controller,
            BattleGrid grid,
            CombatEntity entity,
            EnemyConfig config,
            List<Vector2Int> route)
        {
            // 池复用时先清理上一次的订阅，避免事件重复挂载
            UnsubscribeBlocker();
            if (_entity != null && _entity.Health != null)
            {
                _entity.Health.Died -= HandleDeath;
            }
            // 重置所有状态
            _controller = controller;
            _grid = grid;
            _entity = entity;
            _config = config;
            _route = route;
            _routeIndex = 0;
            _cell = default;
            _nextCell = default;
            _hasNextCell = false;
            _occupiesNextCell = false;
            _blocked = false;
            _occupiedCells.Clear();
            // 路线至少要有起点和终点两个点
            if (_route == null || _route.Count < 2)
            {
                Debug.LogError("敌人路线无效，无法生成。");
                BattleObjectUtility.Destroy(gameObject);
                return;
            }
            // 初始位置：路线第 0 个格子，朝向第 1 个格子
            _cell = _route[0];
            _occupiedCells.Add(_cell);
            GridFacing facing = GetFacing(_cell, _route[1]);
            transform.position = _grid.Map.CellToWorld(_cell)
                                 + Vector3.up * _grid.Map.GetSurfaceHeight(_cell);
            _entity.Initialize(controller.World, Faction.Enemy, config, _cell, facing);
            // 敌人不走自动索敌，目标由阻挡关系显式设置
            _entity.AutoTargeting = false;
            // 订阅死亡事件并注册到网格
            _entity.Health.Died += HandleDeath;
            _grid.RegisterEnemy(_entity, _cell);
        }

        /// <summary>
        /// 回收到对象池前清掉这一局的状态和事件；格子占用由 BattleController 先移除。
        /// 注意：这里不销毁 GameObject，只重置状态，对象会被压回池中复用。
        /// </summary>
        public void ReleaseForPool()
        {
            // 解除阻挡订阅与死亡订阅
            UnsubscribeBlocker();
            if (_entity != null)
            {
                if (_entity.Health != null)
                {
                    _entity.Health.Died -= HandleDeath;
                }
                // 清空当前目标与接触目标，避免下次复用时残留引用
                _entity.SetTarget(null);
                _entity.ClearContactTarget();
            }
            // 重置所有移动/阻挡状态
            _route = null;
            _routeIndex = 0;
            _cell = default;
            _nextCell = default;
            _hasNextCell = false;
            _occupiesNextCell = false;
            _blocked = false;
            _occupiedCells.Clear();
        }
        #endregion

        #region 移动与阻挡主循环

        /// <summary>
        /// 每逻辑 tick 调用：推进移动、检查阻挡、更新占格、到达检测。
        /// 执行顺序：
        /// 1. 已死亡/被阻挡 → 直接返回（被阻挡时不动，由阻挡者决定何时释放）；
        /// 2. 已到路线终点 → 漏怪；
        /// 3. 首次确定下一格时，检查目标格是否有干员，有则停下；
        /// 4. 每 tick 补一次阻挡检查（干员可能在敌人跨格后才部署）；
        /// 5. 朝下一格中心移动；
        /// 6. 跨过格子边界（距离 >= 0.5）后注册双格占用；
        /// 7. 到达下一格中心：更新 _cell / _routeIndex，切换朝向；
        /// 8. 若到达基地格 → 漏怪。
        /// </summary>
        public void Tick()
        {
            // 已死亡或被阻挡时不动
            if (_entity == null || !_entity.IsAlive || _blocked)
            {
                return;
            }
            // 已到路线终点，触发漏怪
            if (_routeIndex >= _route.Count - 1)
            {
                Leak();
                return;
            }
            // 首次确定下一格时，检查目标格是否有干员
            if (!_hasNextCell)
            {
                _nextCell = _route[_routeIndex + 1];

                // 进入目标格之前先尝试阻挡；成功则敌人停留在当前格。
                CombatEntity ally = _grid.GetAllyAt(_nextCell);
                if (ally != null && TryBlockBy(ally))
                {
                    return;
                }

                _hasNextCell = true;
            }

            // 干员可能在敌人已经跨过半个格子后才部署；每 tick 补一次阻挡检查。
            if (_hasNextCell)
            {
                CombatEntity crossingAlly = _grid.GetAllyAt(_nextCell);
                if (crossingAlly != null && TryBlockBy(crossingAlly))
                {
                    return;
                }
            }
            // 朝下一格中心移动
            Vector3 target = _grid.Map.CellToWorld(_nextCell)
                             + Vector3.up * _grid.Map.GetSurfaceHeight(_nextCell);
            float step = _config.moveSpeed * CombatConstants.TickSeconds;
            transform.position = Vector3.MoveTowards(transform.position, target, step);
            // 跨过格子边界（距当前格中心 >= 0.5）后，同时注册两个格子，供两边范围的干员攻击。
            Vector3 currentCenter = _grid.Map.CellToWorld(_cell)
                                    + Vector3.up * _grid.Map.GetSurfaceHeight(_cell);
            if (!_occupiesNextCell && Vector3.Distance(transform.position, currentCenter) >= 0.5f)
            {
                // 跨过格子边界后同时注册两个格子，供两边范围的干员攻击。
                _grid.AddEnemyToCell(_entity, _nextCell);
                if (!_occupiedCells.Contains(_nextCell))
                {
                    _occupiedCells.Add(_nextCell);
                }

                _occupiesNextCell = true;
            }
            // 还没到达下一格中心，本 tick 结束
            if ((transform.position - target).sqrMagnitude > 0.0001f)
            {
                return;
            }
            // 到达下一格：更新当前格、路线索引、朝向，清理双格占位标记
            Vector2Int previous = _cell;
            _grid.RemoveEnemyFromCell(_entity, previous);
            _occupiedCells.Remove(previous);
            _cell = _nextCell;
            _routeIndex++;
            _hasNextCell = false;
            _occupiesNextCell = false;
            GridFacing nextFacing = GetFacing(previous, _cell);
            _entity.SetGridPosition(_cell, nextFacing);
            // 到达基地格触发漏怪
            if (_cell == _grid.Map.BaseCell)
            {
                Leak();
            }
        }
        #endregion

        #region 阻挡管理

        /// <summary>
        /// 尝试被指定干员阻挡：
        /// 1. 已阻挡/已有阻挡者/干员为空/自身已死亡 → 返回 false；
        /// 2. 调用干员的 TryBlock，失败（如干员阻挡数已满）→ 返回 false；
        /// 3. 订阅干员的 BlockReleased 事件，设置自身目标与接触目标。
        /// 成功后敌人停止移动，由阻挡者控制何时释放。
        /// </summary>
        public bool TryBlockBy(CombatEntity ally)
        {
            if (_blocked
                || _blockedBy != null
                || ally == null
                || _entity == null
                || !_entity.IsAlive)
            {
                return false;
            }
            // 干员侧校验（阻挡数上限等），失败则本次阻挡不成立
            if (!ally.TryBlock(_entity))
            {
                return false;
            }

            _blocked = true;
            _blockedBy = ally;
            // 订阅干员的"阻挡释放"事件，干员撤退/死亡时通知自己
            _blockedBy.BlockReleased += HandleBlockerReleased;
            // 设置攻击目标为阻挡者
            _entity.SetTarget(ally);
            _entity.SetContactTarget(ally);
            return true;
        }
        /// <summary>
        /// 主动解除阻挡（漏怪、死亡、被回收前调用）：
        /// 先取消订阅，再清空自身目标，最后通知阻挡者释放。
        /// </summary>
        public void ReleaseBlock()
        {
            CombatEntity blocker = _blockedBy;
            UnsubscribeBlocker();

            if (blocker != null)
            {
                _entity?.SetTarget(null);
                _entity?.ClearContactTarget();
                blocker.ReleaseBlock(_entity);
            }
        }
        /// <summary>
        /// 干员释放阻挡时的事件回调：
        /// 事件参数是被释放的敌人，只处理自己这一个阻挡关系（同一干员可能挡了多个敌人）。
        /// 收到后清空目标、取消订阅。
        /// </summary>
        private void HandleBlockerReleased(CombatEntity enemy)
        {
            // 事件参数是被释放的敌人；只处理自己这一个阻挡关系。
            if (enemy != _entity)
            {
                return;
            }

            UnsubscribeBlocker();
            _entity?.SetTarget(null);
            _entity?.ClearContactTarget();
        }
        /// <summary>
        /// 取消阻挡订阅并重置阻挡状态。
        /// 幂等：重复调用安全（_blockedBy 为空时直接返回）。
        /// </summary>
        private void UnsubscribeBlocker()
        {
            if (_blockedBy != null)
            {
                _blockedBy.BlockReleased -= HandleBlockerReleased;
            }

            _blockedBy = null;
            _blocked = false;
        }
        #endregion

        #region 事件与清理

        /// <summary>
        /// 漏怪：先解除阻挡，再上报 BattleController 扣生命/移除自己。
        /// </summary>
        private void Leak()
        {
            ReleaseBlock();
            _controller.OnEnemyLeaked(this);
        }
        /// <summary>
        /// 死亡：先解除阻挡，再上报 BattleController 计入击杀/移除自己。
        /// 与 Leak 的区别：Leak 是"抵达基地"，HandleDeath 是"血量归零"，
        /// 两者都从敌人列表移除，但 Leak 会扣生命、HandleDeath 不会。
        /// </summary>
        private void HandleDeath()
        {
            ReleaseBlock();
            _controller.OnEnemyRemoved(this);
        }
        /// <summary>
        /// 销毁时取消阻挡订阅，避免阻挡者持有已销毁敌人的事件引用。
        /// 注意：死亡订阅（Health.Died）由 CombatEntity/Health 自身管理，这里不处理。
        /// </summary>
        private void OnDestroy()
        {
            UnsubscribeBlocker();
        }
        #endregion

        #region 工具方法

        /// <summary>
        /// 根据相邻两格计算朝向：优先判断水平（左右），水平无变化再判断垂直（上下）。
        /// 用于敌人移动时更新朝向，以及初始化时的初始朝向。
        /// </summary>
        private static GridFacing GetFacing(Vector2Int from, Vector2Int to)
        {
            Vector2Int delta = to - from;
            if (delta.x > 0)
            {
                return GridFacing.Right;
            }

            if (delta.x < 0)
            {
                return GridFacing.Left;
            }

            if (delta.y > 0)
            {
                return GridFacing.Up;
            }

            return GridFacing.Down;
        }
        #endregion
    }
}
