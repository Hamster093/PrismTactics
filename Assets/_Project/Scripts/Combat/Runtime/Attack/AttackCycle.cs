/****************************************************
    文件：AttackCycle.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：单个单位的攻击循环：前摇、伤害结算、后摇与攻击间隔。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 单次攻击循环：前摇 -> 伤害结算 -> 后摇。后摇由攻击间隔减去前摇得到，
    /// 保证每次攻击起手严格相隔一个攻击间隔。
    ///
    /// 核心机制（状态机 + 累计计数）：
    ///   Ready ──(有目标)──► Windup ──(_cycleTicks 到 windup)──► Recovery
    ///     ▲                                                       │
    ///     │                                      (_cycleTicks 到 interval)
    ///     └──────────────(有目标则 StartWindup / 无目标则 Reset)──┘
    ///
    /// 关键设计：_cycleTicks 是"本轮起手开始的累计 tick"，跨 Windup 和 Recovery 两个阶段共享，
    /// 不是阶段内计数。StartWindup 时归零，Recovery 阶段继续累加到 interval 才再次起手。
    /// 因此两次 PerformAttack 之间严格相隔 interval ticks（起手到起手），而不是"后摇独立计时"。
    ///
    /// 职责边界：
    /// - 本类只管"时序状态机"，不关心目标选择、伤害计算、动画播放；
    /// - 目标有效性、伤害结算、动画驱动标记由 IAttackCycleOwner 提供；
    /// - 攻速缩放只影响本类的 tick 数，不改 owner 的任何状态。
    ///
    /// 设计取舍：
    /// - 用累计 _cycleTicks 而非分阶段计数器：Recovery 判断只需一个 `_cycleTicks >= interval`，
    ///   避免"前摇结束时刻"与"后摇开始时刻"的边界重复计数；
    /// - 前摇为 0 时在 StartWindup 内同步完成结算并直接进 Recovery，不经过 Windup 状态；
    /// - 攻速倍率有 0.1f 下限，防止除零和无限快；这是硬编码上限（最多 10 倍速）。
    /// </summary>
    public sealed class AttackCycle
    {
        #region 基础配置字段
        /// <summary>
        /// 基础攻击间隔（tick），不受攻速影响；构造时至少 1，EnsureIntervalAtLeast 只会增大它。
        /// 当前实际间隔 _attackIntervalTicks 由它除以攻速倍率得到。
        /// </summary>
        private int _baseAttackIntervalTicks;// 基础攻击间隔
        /// <summary>
        /// 基础前摇（tick），readonly；构造时 Clamp 到 [0, 基础间隔]。
        /// 注意：它是基准值，当前实际前摇 _windupTicks 由它除以攻速倍率得到。
        /// </summary>
        private readonly int _baseWindupTicks;// 基础前摇时长
        #endregion

        #region 运行时状态字段
        private int _attackIntervalTicks;// 当前攻击间隔（受攻速影响）
        private int _windupTicks;// 当前前摇时长（受攻速影响）
        /// <summary>
        /// 攻速倍率，1 为正常速度；SetSpeedMultiplier 会 Clamp 到 >= 0.1f。
        /// 注意：Reset 不会重置它，持续技能结束需外部显式 SetSpeedMultiplier(1f)。
        /// </summary>
        private float _speedMultiplier = 1f;
        /// <summary>
        /// 本轮攻击循环的累计 tick 计数：StartWindup 时归零，Windup 和 Recovery 两个阶段共享递增。
        /// 注意：它不是"当前阶段内计数"——进入 Recovery 时它已接近 _windupTicks，继续涨到 _attackIntervalTicks 才再次起手。
        /// 这是"起手到起手严格相隔一个间隔"的实现基础。
        /// </summary>
        private int _cycleTicks;// 当前阶段已走过的 tick 数
        #endregion

        #region 公开属性
        // 当前攻击循环状态
        public AttackCycleState State { get; private set; } = AttackCycleState.Ready;
        public int AttackIntervalTicks => _attackIntervalTicks;/// <summary>当前实际攻击间隔（tick），已含攻速缩放。</summary>
        public int WindupTicks => _windupTicks;/// <summary>当前实际前摇（tick），已含攻速缩放。</summary>
        public int RecoveryTicks => Mathf.Max(0, _attackIntervalTicks - _windupTicks);// 后摇时长 = 攻击间隔 - 前摇
        public int CycleTicks => _cycleTicks;// 本轮循环的累计 tick 计数
        #endregion

        #region 构造与配置
        /// <summary>
        /// 构造攻击循环。
        /// 参数兜底：攻击间隔至少 1 tick，前摇 Clamp 到 [0, 基础间隔]，保证前摇不超过间隔。
        /// 初始当前间隔/前摇等于基础值（攻速倍率默认 1）。
        /// </summary>
        // 构造：间隔至少 1 tick，前摇夹在 [0, 间隔] 之间
        public AttackCycle(int attackIntervalTicks, int windupTicks)
        {
            // 至少 1 tick，避免间隔为 0 导致同 tick 无限循环或除零
            _baseAttackIntervalTicks = Mathf.Max(1, attackIntervalTicks);
            // 前摇不允许超过基础间隔，也不允许为负。
            _baseWindupTicks = Mathf.Clamp(windupTicks, 0, _baseAttackIntervalTicks);
            _attackIntervalTicks = _baseAttackIntervalTicks;
            _windupTicks = _baseWindupTicks;
        }

        /// <summary>
        /// 设置攻速倍率，1 为正常速度；持续技能结束时会恢复为 1。
        /// 倍率下限硬编码为 0.1f：防止除零，同时把最大加速限制在 10 倍。
        /// 副作用：立即 RecalculateTicks 重算当前间隔/前摇，并可能截断 _cycleTicks。
        /// </summary>
        public void SetSpeedMultiplier(float speedMultiplier)
        {
            // 0.1f 是硬编码下限：既防除零，也限制最大 10 倍速。
            _speedMultiplier = Mathf.Max(0.1f, speedMultiplier);
            RecalculateTicks();
        }

        /// <summary>
        /// 保证攻击间隔至少覆盖指定时长，通常用于让 Spine 的 Attack 动画完整播放。
        /// 只增不减：目标值小于等于当前基础间隔时直接返回，不会缩小间隔。
        /// 副作用：修改 _baseAttackIntervalTicks 并 RecalculateTicks；多次调用取最大值。
        /// 注意：修改的是基础间隔，会与攻速倍率一起影响当前间隔；已缩小的间隔无法通过再次调用恢复。
        /// </summary>
        public void EnsureIntervalAtLeast(int ticks)
        {
            // 至少 1 tick，避免传入 0 或负数导致间隔非法。
            int target = Mathf.Max(1, ticks);
            // 只增不减：目标不超过当前基础间隔时无操作。
            if (target <= _baseAttackIntervalTicks)
            {
                return;
            }

            _baseAttackIntervalTicks = target;
            RecalculateTicks();
        }
        #endregion

        #region 状态机推进
        /// <summary>
        /// 每个逻辑 tick 推进状态机：就绪 -> 前摇 -> 结算 -> 后摇 -> 就绪。
        ///
        /// 执行顺序：
        /// 1. owner 判空，空则直接返回；
        /// 2. Ready：有有效目标则 StartWindup，否则保持；
        /// 3. Windup：目标丢失且非动画锁定则 Reset；否则 _cycleTicks++，到点 PerformAttack 并切 Recovery；
        /// 4. Recovery：_cycleTicks++，未到 interval 则继续；到点后按有无目标再次起手或 Reset。
        ///
        /// 前置条件：owner 非 null；本方法每 tick 由外部驱动一次。
        /// 副作用：修改 State、_cycleTicks，可能触发 owner.PerformAttack()。
        /// 注意：Ready 状态不推进 _cycleTicks；Windup 到 Recovery 切换时 _cycleTicks 不归零，
        ///       由 Recovery 继续累加到 interval，这是"起手到起手相隔一个间隔"的关键。
        /// </summary>
        public void Tick(IAttackCycleOwner owner)
        {
            // 1. owner 判空：空则本 tick 不推进，避免空引用。
            if (owner == null)
            {
                return;
            }

            switch (State)
            {
                // 2. 就绪：有有效目标则进入前摇；无目标则什么都不做，_cycleTicks 保持 0。
                case AttackCycleState.Ready:
                    if (owner.HasValidTarget())
                    {
                        StartWindup(owner);
                    }
                    break;
                // 前摇：普通攻击可因目标丢失重置；Spine 动画攻击一旦开始就播完。
                case AttackCycleState.Windup:
                    // 目标丢失且不是动画锁定攻击时，整轮重置回 Ready。
                    if (!owner.HasValidTarget() && !IsAnimationAttackLocked(owner))
                    {
                        Reset();
                        break;
                    }
                    // 前摇推进：注意 _cycleTicks 跨阶段累计，切 Recovery 时不归零。
                    _cycleTicks++;
                    if (_cycleTicks >= _windupTicks)
                    {
                        // 前摇走满，结算伤害并进入后摇。
                        // 注意：PerformAttack 只触发一次，后续 tick 都在 Recovery 分支。
                        owner.PerformAttack();
                        State = AttackCycleState.Recovery;
                    }
                    break;
                // 后摇：走满攻击间隔后，有目标则再次起手，否则重置
                case AttackCycleState.Recovery:
                    // 继续累加 _cycleTicks：进入 Recovery 时它已接近 _windupTicks，
                    // 因此这里走到 _attackIntervalTicks 时，实际后摇时长约为 interval - windup。
                    _cycleTicks++;
                    if (_cycleTicks < _attackIntervalTicks)
                    {
                        break;
                    }
                    // 走满一个完整间隔：有目标则立即起手下一轮，无目标则整轮重置。
                    if (owner.HasValidTarget())
                    {
                        StartWindup(owner);
                    }
                    else
                    {
                        Reset();
                    }
                    break;
            }
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 按当前攻速倍率重算当前间隔和前摇。
        /// 注意：_cycleTicks 被截断到当前间隔，防止周期缩短后计数越界；
        ///       截断后若恰好 >= 新间隔，下一次 Tick 会立即触发状态转换。
        /// </summary>
        private void RecalculateTicks()
        {
            // 当前间隔 = 基础间隔 / 攻速，四舍五入且至少 1 tick。
            _attackIntervalTicks = Mathf.Max(1,Mathf.RoundToInt(_baseAttackIntervalTicks / _speedMultiplier));
            // 当前前摇 = 基础前摇 / 攻速，四舍五入后 Clamp 到 [0, 当前间隔]；
            // 攻速很快时前摇可能被四舍五入为 0，此时 StartWindup 会同步完成结算。
            _windupTicks = Mathf.Clamp( Mathf.RoundToInt(_baseWindupTicks / _speedMultiplier),0, _attackIntervalTicks);
            // 周期缩短时截断计数，避免越界；下一次 Tick 会按新间隔重新判断。
            _cycleTicks = Mathf.Min(_cycleTicks, _attackIntervalTicks);
        }
        /// <summary>
        /// 重置为就绪状态并清空累计计数。
        /// 注意：不重置 _speedMultiplier、_attackIntervalTicks、_windupTicks，
        ///       攻速和间隔配置由外部显式恢复（如持续技能结束时 SetSpeedMultiplier(1f)）。
        /// 副作用：State 回到 Ready，_cycleTicks 归零；可能触发下一次 Tick 重新起手。
        /// </summary>
        public void Reset()
        {
            State = AttackCycleState.Ready;
            _cycleTicks = 0;
        }


        /// <summary>
        /// 判断 owner 是否为"动画驱动伤害"且当前处于锁定状态。
        /// 用途：Spine 动画攻击一旦开始就播完，即使目标丢失也不在前摇中途 Reset。
        /// 注意：这是接口抽象泄漏——IAttackCycleOwner 不表达 AnimationDrivenDamage 语义，
        ///       这里强依赖具体类型 CombatEntity。若未来有其他 IAttackCycleOwner 实现，需同步处理
        /// </summary>
        private static bool IsAnimationAttackLocked(IAttackCycleOwner owner)
        {
            return owner is CombatEntity entity && entity.AnimationDrivenDamage;
        }
        /// <summary>
        /// 进入前摇：设置 State 和 _cycleTicks = 0。
        /// 特例：前摇为 0 时同步完成 PerformAttack 并直接切 Recovery，不经过 Windup 状态。
        /// 注意：本方法在同一个 tick 内可能完成"起手 -> 结算 -> 进后摇"，这是前摇为 0 的设计。
        /// </summary>
        private void StartWindup(IAttackCycleOwner owner)
        {
            State = AttackCycleState.Windup;
            _cycleTicks = 0;

            if (_windupTicks > 0)
            {
                return;
            }

            owner.PerformAttack();
            State = AttackCycleState.Recovery;
        }
        #endregion
    }
}
