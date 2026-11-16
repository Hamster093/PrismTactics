/****************************************************
    文件：CombatWorld.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗世界：固定 tick 驱动、索敌扫描、伤害管线、实体注册与格子空间。
*****************************************************/

using System;
using UnityEngine;

namespace PrismTactics.Combat
{
    // 战斗世界总入口，负执行顺序保证先于普通脚本更新
    [DefaultExecutionOrder(-50)]
    public sealed class CombatWorld : MonoBehaviour
    {
        #region 序列化字段
        // 是否由本组件在 Update 里自动驱动 tick；关掉后可手动 TickOnce（如编辑器/测试）
        [SerializeField] private bool _autoTick = true;
        // 是否每秒打印一次战斗状态日志
        [SerializeField] private bool _logTicks;
        #endregion

        #region 核心子系统（公开只读）
        // 实体注册表
        public CombatRegistry Registry { get; } = new CombatRegistry();
        // 伤害结算管线
        public DamagePipeline DamagePipeline { get; } = new DamagePipeline();
        // 固定 30Hz 战斗时钟
        public CombatClock Clock { get; } = new CombatClock();
        #endregion

        #region 空间与索敌
        /// <summary>
        /// 可选的空间查询实现。M0 为空时使用世界距离；M1 接入格子后由 BattleGrid 实现。
        /// </summary>
        public ICombatSpace Space { get; set; }

        private TargetingService _targeting;// 索敌服务，延迟创建（依赖 Registry 与 Space）

        public TargetingService Targeting => _targeting ?? (_targeting = new TargetingService(Registry, Space));// 懒加载索敌服务，首次访问时构造
        #endregion

        #region 事件
        public event Action<int> Ticked;// 每个逻辑 tick 结束时触发，参数为当前 tick 序号
        #endregion

        #region Unity 生命周期
        /// <summary>
        /// 自动模式下每渲染帧累积时间并执行对应数量的逻辑 tick。
        /// 用累积器而非"每帧一 tick"，是为了保证逻辑频率稳定在 30Hz，与渲染帧率解耦。
        /// </summary>
        private void Update()
        {
            if (!_autoTick)
            {
                return;
            }

            int pendingTicks = Clock.Accumulate(Time.deltaTime);
            for (int i = 0; i < pendingTicks; i++)
            {
                TickOnce();
            }
        }
        /// <summary>
        /// 销毁时清理热更 Tick 服务，避免全局状态残留。
        /// </summary>
        private void OnDestroy()
        {
            HotUpdateTickService.Clear();
        }
        #endregion

        #region 逻辑推进

        /// <summary>
        /// 精确推进一个 30Hz 逻辑 tick。测试和编辑器可以直接调用。
        /// 执行顺序：
        /// 1. 推进时钟；
        /// 2. 按间隔执行索敌扫描（不是每 tick 都扫，降低开销）；
        /// 3. 分发实体 Tick；
        /// 4. 按需打印状态日志；
        /// 5. 驱动热更 Tick 服务；
        /// 6. 触发 Ticked 事件，通知外部（如 BattleController）。
        /// </summary>
        public void TickOnce()
        {
            Clock.AdvanceOneTick();
            int tick = Clock.TickCount;
            // 索敌按固定间隔执行，避免每 tick 全量扫描带来的性能压力
            if (tick % CombatConstants.TargetScanIntervalTicks == 0)
            {
                Targeting.ScanAll();
            }
            // 分发到所有已注册实体
            Registry.TickAll(tick);
            // 按秒打印状态日志（tick 序号对 TickRate 取模）
            if (_logTicks && tick % CombatConstants.TickRate == 0)
            {
                Debug.Log($"[战斗] tick={tick} 实体={Registry.Count}");
            }
            // 热更逻辑的 Tick 钩子
            HotUpdateTickService.Tick(tick);
            // 通知外部监听者（BattleController 等）
            Ticked?.Invoke(tick);
        }
        #endregion

        #region 范围判定
        /// <summary>
        /// 判定 target 是否在 source 攻击范围内；优先用格子空间，否则走世界距离。
        /// 源或目标任一无效/已死亡时直接返回 false。
        /// </summary>
        public bool IsTargetInRange(CombatEntity source, CombatEntity target)
        {
            // 源无效或已死亡，无法判定
            if (source == null || !source.IsAlive || source.Config == null)
            {
                return false;
            }
            // 目标无效或已死亡，视为不在范围内
            if (target == null || !target.IsAlive)
            {
                return false;
            }
            // 优先使用空间接口（如格子空间），支持非欧几里得距离/高台规则
            if (Space != null)
            {
                return Space.IsInRange(source, target);
            }
            // 兜底：用配置里的世界距离做圆形范围判定（比较平方，避免开方）
            float range = source.Config.attackRange;
            return (target.TargetPosition - source.TargetPosition).sqrMagnitude <= range * range;
        }
        #endregion

        #region 实体注册
        /// <summary>
        /// 将实体加入注册表。实体生命周期内只会注册一次。
        /// </summary>
        public void Register(CombatEntity entity)
        {
            Registry.Register(entity);
        }
        /// <summary>
        /// 将实体移出注册表。通常在实体死亡/销毁时调用。
        /// </summary>
        public void Unregister(CombatEntity entity)
        {
            Registry.Unregister(entity);
        }
        #endregion
    }
}
