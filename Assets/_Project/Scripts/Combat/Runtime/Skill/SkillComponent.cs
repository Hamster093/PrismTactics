/****************************************************
    文件：SkillComponent.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 00:20:00
	功能：技能运行时：SP 充能、手动/自动触发、持续时间与技能切换。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 技能运行时组件：挂在战斗实体上，负责单个实体"当前技能"的 SP 充能、触发与持续时间管理。
    ///
    /// 核心机制（状态机）：
    ///   Charging ──(SP 满 && Manual)──► Ready ──(TryActivate 成功)──► Charging
    ///      │                                                              ▲
    ///      └──(SP 满 && Auto)──► TryActivate ──(TimedBuff)──► Active ────┘
    ///                                                        │
    ///                                                        └─(duration 归零)─► Charging
    ///
    /// 充能来源（SpRecoveryType）：
    ///   Auto   —— Tick 中按 spPerSecond * TickSeconds 累积，满整数才加，余数留到下次；
    ///   Attack —— 由外部 OnAttackPerformed 回调 +1；
    ///   Defend —— 由外部 OnDamaged 回调 +1。
    ///
    /// 职责边界：
    /// - 本类只管"当前技能的 SP / 状态 / 持续时间"；
    /// - 技能效果的实际执行（伤害、buff、热更 handler）委托给 CombatEntity 或 CombatHotUpdateRegistry；
    /// - 技能配置数据来自 SkillConfig（ScriptableObject 或配置表），本类不校验配置合法性。
    ///
    /// 设计取舍：
    /// - SP 用 float 累积器而非每 tick 直接加整数：低 spPerSecond 时避免每 tick 取整为 0 导致永远充不满；
    /// - 热更 effectKey 存在时优先走注册表，且成功后不进入 Active 持续时间——持续时间由 handler 自行管理；
    /// - Active 期间也触发 SpChanged：SpFillRatio 在 Active 下返回剩余持续时间比例，UI 需要每 tick 刷新。
    /// </summary>
    public sealed class SkillComponent : MonoBehaviour
    {
        #region 依赖与状态字段
        /// <summary>技能拥有者；由 Initialize 注入，Tick 中用于判活和委托效果执行。</summary>
        private CombatEntity _owner;
        /// <summary>该实体拥有的技能配置数组；由 Initialize 注入，SelectSkill 按索引取用。</summary>
        private SkillConfig[] _skills;
        /// <summary>当前选中的技能在 _skills 中的索引；SelectSkill 会 Clamp 到合法范围。</summary>
        private int _skillIndex;
        /// <summary>
        /// SP 浮点累积器：Auto 恢复时累加 spPerSecond * TickSeconds，取整数部分加到 CurrentSp，
        /// 小数部分保留到下次，避免低恢复速率下每 tick FloorToInt 为 0 导致永远充不满。
        /// </summary>
        private float _spAccumulator;
        /// <summary>TimedBuff 生效期间的剩余 tick 数；归零时清除 buff 并回到 Charging。</summary>
        private int _remainingDurationTicks;
        #endregion

        #region 公开属性与事件
        /// <summary>当前选中的技能配置；SelectSkill 后更新，无技能时为 null。</summary>
        public SkillConfig Config { get; private set; }
        public int CurrentSp { get; private set; } /// <summary>当前 SP 值；不超过 MaxSp，SelectSkill 时按 initialSp 初始化。</summary>
        public int MaxSp => Config != null ? Config.MaxSp : 0;/// <summary>SP 上限；Config 为 null 时返回 0，避免 UI 除零。</summary>
        public SkillState State { get; private set; } = SkillState.Charging;/// <summary>技能状态机当前状态；初始为 Charging。</summary>
        public int RemainingDurationTicks => _remainingDurationTicks;/// <summary>TimedBuff 剩余持续时间 tick 数；非 Active 状态下为 0。</summary>
        /// <summary>
        /// 是否可激活：要求有配置、非 Active、SP 已满。
        /// 注意：未校验 activationType，Auto 类型在 SP 满时理论上也能被外部手动 TryActivate（事实陈述，未改逻辑）。
        /// </summary>
        public bool CanActivate => Config != null
            && State != SkillState.Active
            && CurrentSp >= MaxSp;
        public int SkillCount => _skills != null ? _skills.Length : 0;/// <summary>技能总数；_skills 为 null 时返回 0。</summary>
        public int SkillIndex => _skillIndex; /// <summary>当前技能索引；供 UI 显示技能槽位。</summary>
        public event System.Action SpChanged;// SP 或技能状态变化，供技力条事件驱动刷新
        public bool ShowSpBar => Config != null && MaxSp > 0; /// <summary>是否显示 SP 条：有配置且 MaxSp > 0 才显示。</summary>
        /// <summary>
        /// SP 条填充比例，0~1。
        /// 注意：该属性有两种语义——
        ///   Active 状态下返回"剩余持续时间 / 总持续时间"（持续时间条）；
        ///   其他状态下返回"CurrentSp / MaxSp"（充能条）。
        /// 命名与语义存在偏差，UI 侧需知晓此切换。
        /// </summary>
        public float SpFillRatio
        {
            get
            {
                // MaxSp 为 0 时直接返回 0，避免除零。
                if (MaxSp <= 0)
                {
                    return 0f;
                }
                // Active 状态显示持续时间比例，而非 SP 比例。
                if (State == SkillState.Active)
                {
                    // 至少 1 tick，避免 durationTicks 配置为 0 时除零。
                    int duration = Mathf.Max(1, Config.durationTicks);
                    return Mathf.Clamp01(_remainingDurationTicks / (float)duration);
                }

                return Mathf.Clamp01(CurrentSp / (float)MaxSp);
            }
        }
        #endregion

        #region 初始化与技能切换
        /// <summary>
        /// 注入拥有者和技能数组，并默认选中第 0 个技能。
        /// 副作用：重置 _skillIndex、Config、SP、状态，并触发 SpChanged。
        /// </summary>
        public void Initialize(CombatEntity owner, SkillConfig[] skills)
        {
            _owner = owner;
            _skills = skills;
            _skillIndex = 0;
            SelectSkill(0);
        }
        /// <summary>
        /// 切换到指定索引的技能，并重置该技能的 SP / 状态 / 持续时间。
        /// 前置条件：_skills 非空；为空时清空 Config、SP 和状态，但不重置 _skillIndex（事实陈述，未改逻辑）。
        /// 副作用：更新 Config、CurrentSp、State、_remainingDurationTicks，触发 SpChanged。
        /// 注意：初始状态只按"SP 是否满"判定 Ready/Charging，不区分 activationType；
        ///       Auto 类型若 initialSp 满，会先停在 Ready，等下一次 RefreshState 才自动触发。
        /// </summary>
        public void SelectSkill(int index)
        {
            if (_skills == null || _skills.Length == 0)
            {
                Config = null;
                CurrentSp = 0;
                State = SkillState.Charging;
                return;
            }
            // Clamp 索引到合法范围，防止外部传入越界值。
            _skillIndex = Mathf.Clamp(index, 0, _skills.Length - 1);
            Config = _skills[_skillIndex];
            // initialSp 兜底 Clamp 到 [0, MaxSp]，防止配置写超。
            CurrentSp = Config != null ? Mathf.Clamp(Config.initialSp, 0, Config.MaxSp) : 0;
            _spAccumulator = 0f;
            // 初始状态只看 SP 是否满，不区分 Auto/Manual。
            State = CurrentSp >= MaxSp ? SkillState.Ready : SkillState.Charging;
            _remainingDurationTicks = 0;
            SpChanged?.Invoke();
        }
        #endregion

        #region 技能触发
        /// <summary>
        /// 尝试激活当前技能。
        ///
        /// 执行顺序：
        /// 1. 检查 CanActivate；
        /// 2. 若配置了 effectKey，优先走热更注册表执行，成功则重置 SP 并返回；
        /// 3. 否则按内置 effectType 分支执行（NextAttack / InstantDamage / TimedBuff）；
        /// 4. 统一重置 SP、累积器，按是否进入 Active 决定状态；
        /// 5. 触发 SpChanged。
        ///
        /// 返回 false 的情况：不可激活、热更 handler 未注册、InstantDamage 无敌人或执行失败。
        /// 副作用：会执行技能效果、修改 CurrentSp/State/_remainingDurationTicks，触发 SpChanged。
        /// 设计取舍：effectKey 非空时不会回退到内置 effectType 逻辑，未注册直接报错返回 false。
        /// </summary>
        public bool TryActivate()
        {
            // 1. 前置检查：配置存在、非 Active、SP 满。
            if (!CanActivate)
            {
                return false;
            }
            // 2. 热更分支：effectKey 非空时优先走注册表。
            if (!string.IsNullOrEmpty(Config.effectKey))
            {
                var context = new HotUpdateSkillContext(_owner, Config);
                if (CombatHotUpdateRegistry.TryExecute(context))
                {
                    // 热更技能成功后直接回 Charging，不进入 Active 持续时间——持续时间由 handler 自行管理。
                    CurrentSp = 0;
                    _spAccumulator = 0f;
                    State = SkillState.Charging;
                    SpChanged?.Invoke();
                    return true;
                }
                // 未注册处理器视为配置错误，报错并返回 false，不回退到内置逻辑。
                Debug.LogError("技能 effectKey 没有注册处理器：" + Config.effectKey);
                return false;
            }
            // 3. 内置分支：InstantDamage 先检查范围内是否有敌人，避免空放。
            if (Config.effectType == SkillEffectType.InstantDamage && !_owner.HasEnemyInSkillRange(Config.rangeCells))
            {
                return false;
            }

            if (Config.effectType == SkillEffectType.NextAttack)
            {
                // NextAttack 只挂到下一次普攻，不进入 Active 持续时间。
                _owner.SetNextAttack(Config);
            }
            else if (Config.effectType == SkillEffectType.InstantDamage)
            {
                // 实际伤害执行；失败则直接返回，不重置 SP。
                if (!_owner.ExecuteInstantDamage(Config))
                {
                    return false;
                }
            }
            else if (Config.effectType == SkillEffectType.TimedBuff)
            {
                // TimedBuff 进入 Active 状态，按 durationTicks 计时。
                _owner.ApplyTimedSkill(Config);
                State = SkillState.Active;
                // 至少 1 tick，避免配置为 0 导致立即结束或除零。
                _remainingDurationTicks = Mathf.Max(1, Config.durationTicks);
            }
            // 4. 统一重置 SP；只有 TimedBuff 保留 Active，其余回 Charging。
            CurrentSp = 0;
            _spAccumulator = 0f;
            if (State != SkillState.Active)
            {
                State = SkillState.Charging;
            }
            // 5. 通知 UI 刷新。
            SpChanged?.Invoke();
            return true;
        }
        #endregion

        #region 运行推进
        /// <summary>
        /// 每 tick 推进技能状态：
        /// - Active：递减持续时间，归零时清除 buff 并回 Charging；
        /// - Auto 恢复：按 spPerSecond * TickSeconds 累积 SP；
        /// - 最后 RefreshState 刷新状态（Auto 满 SP 会自动触发）。
        /// 前置条件：Config 和 _owner 均非 null 且 _owner 存活；否则直接返回。
        /// 副作用：修改 SP、状态、持续时间，可能触发技能执行，触发 SpChanged。
        /// </summary>
        public void Tick()
        {
            // 配置缺失或拥有者死亡时不推进。
            if (Config == null || _owner == null || !_owner.IsAlive)
            {
                return;
            }
            // Active 状态：只处理持续时间倒计时，不充能。
            if (State == SkillState.Active)
            {
                _remainingDurationTicks--;
                if (_remainingDurationTicks <= 0)
                {
                    _owner.ClearTimedSkill();
                    State = SkillState.Charging;
                    CurrentSp = 0;
                }
                // Active 期间也通知 UI：SpFillRatio 此时表示持续时间比例。
                SpChanged?.Invoke();
                return;
            }
            // Auto 恢复：累积浮点 SP，取整数部分，余数留到下次，避免低速率下永远取整为 0。
            if (Config.spRecoveryType == SpRecoveryType.Auto)
            {
                _spAccumulator += Config.spPerSecond * CombatConstants.TickSeconds;
                int gained = Mathf.FloorToInt(_spAccumulator);
                if (gained > 0)
                {
                    _spAccumulator -= gained;
                    AddSp(gained);
                }
            }
            // 刷新状态：Auto 满 SP 会在这里自动触发。
            RefreshState();
        }
        #endregion

        #region 外部事件回调
        /// <summary>
        /// 普攻命中后由外部调用：Attack 恢复类型 +1 SP。
        /// 前置条件：有配置、非 Active、恢复类型为 Attack；否则不处理。
        /// </summary>
        public void OnAttackPerformed()
        {
            if (Config == null || State == SkillState.Active || Config.spRecoveryType != SpRecoveryType.Attack)
            {
                return;
            }
            // 硬编码 +1：每次普攻固定回 1 SP，未做可配置化。
            AddSp(1);
        }
        /// <summary>
        /// 受击后由外部调用：Defend 恢复类型 +1 SP。
        /// 前置条件：有配置、非 Active、恢复类型为 Defend；否则不处理。
        /// </summary>
        public void OnDamaged()
        {
            if (Config == null || State == SkillState.Active || Config.spRecoveryType != SpRecoveryType.Defend)
            {
                return;
            }

            AddSp(1);
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 增加 SP 并刷新状态，最后触发 SpChanged。
        /// 副作用：CurrentSp 被 Clamp 到 MaxSp；可能因 RefreshState 触发 Auto 技能执行。
        /// </summary>
        private void AddSp(int amount)
        {
            CurrentSp = Mathf.Min(MaxSp, CurrentSp + amount);
            RefreshState();
            SpChanged?.Invoke();
        }
        /// <summary>
        /// 按当前 SP 和 activationType 刷新状态：
        /// - Active 直接返回，不打断持续时间；
        /// - SP 未满 → Charging；
        /// - SP 满且 Auto → 自动 TryActivate；
        /// - SP 满且 Manual → Ready。
        /// 注意：本方法访问 Config.activationType 未做 null 检查，依赖调用链（AddSp/Tick）的前置检查；
        ///       若未来有新的调用方绕过前置检查，可能 NRE（事实陈述，未改逻辑）。
        /// </summary>
        private void RefreshState()
        {
            // Active 期间不改变状态，避免打断持续时间。
            if (State == SkillState.Active)
            {
                return;
            }

            if (CurrentSp < MaxSp)
            {
                State = SkillState.Charging;
                return;
            }
            // SP 满：Auto 自动触发，Manual 停在 Ready 等外部调用。
            if (Config.activationType == SkillActivationType.Auto)
            {
                TryActivate();
            }
            else
            {
                State = SkillState.Ready;
            }
        }
        #endregion
    }
}
