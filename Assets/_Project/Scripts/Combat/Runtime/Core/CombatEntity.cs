/****************************************************
    文件：CombatEntity.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗实体：属性、生命、索敌、攻击循环、阻挡、技能与伤害结算。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    // 战斗单位：整合生命、属性、攻击循环、索敌、阻挡与技能
    [DisallowMultipleComponent]
    public sealed class CombatEntity : MonoBehaviour, ITargetable, IAttackCycleOwner
    {
        private sealed class TimedModifier
        {
            public StatType Stat;
            public float Flat;
            public float Percent;
            public int RemainingTicks;
            public object Source;
        }

        private readonly List<TimedModifier> _timedModifiers = new List<TimedModifier>();
        private readonly List<TimedModifier> _timedSpeedModifiers = new List<TimedModifier>();

        [SerializeField] private Faction _faction = Faction.Ally;
        [SerializeField] private UnitConfig _config;

        private readonly List<CombatEntity> _splashBuffer = new List<CombatEntity>();
        private readonly Dictionary<CombatEntity, int> _blockedEnemies =
            new Dictionary<CombatEntity, int>();
        private readonly List<CombatEntity> _attackTargets = new List<CombatEntity>();
        private CombatEntity _contactTarget;
        private readonly object _skillStatSource = new object();

        private CombatWorld _world;
        public CombatWorld World => _world;
        private float _nextAttackMultiplier = 1f;
        private int _nextAttackExtraHits;
        private int _timedExtraHits;
        private bool _attacksAllInRange;
        private float _healOnAttackPercent;
        private bool _animationDrivenDamage;
        private bool _projectileDrivenDamage;
        private CombatEntity _animationAttackTarget;
        private float _skillAttackSpeedPercent;
        // 持续技能期间的多目标与弹道染色
        private int _timedMultiTarget;
        private Color _timedProjectileTint = Color.white;

        /// <summary>当前技能要求的弹道颜色（白色表示不染色）。</summary>
        public Color ProjectileTint => _timedProjectileTint;

        /// <summary>当前技能是否要求多目标攻击（>1 表示随机多目标）。</summary>
        public int TimedMultiTarget => _timedMultiTarget;

        /// <summary>多目标技能的发射间隔（秒）；0 表示不走自动发射。</summary>
        public float SkillVolleyInterval { get; private set; }

        public Faction Faction => _faction;
        public UnitConfig Config => _config;
        public HealthComponent Health { get; private set; }
        public StatBlock Stats { get; private set; }
        public AttackCycle AttackCycle { get; private set; }
        public CombatEntity CurrentTarget { get; private set; }
        public SkillComponent Skills { get; private set; }

        public Vector2Int GridPosition { get; private set; }
        public GridFacing Facing { get; private set; } = GridFacing.Right;

        // 是否允许自动索敌；敌人由阻挡逻辑手动设置目标，因此关闭
        public bool AutoTargeting { get; set; } = true;

        public int BlockCount => _config != null ? _config.blockCount : 0;
        public int BlockCapacity => Mathf.Max(0, BlockCount);
        public int UsedBlockWeight { get; private set; }
        public int BlockWeight => _config is EnemyConfig enemyConfig
            ? Mathf.Max(1, enemyConfig.blockWeight)
            : 1;
        public bool AnimationDrivenDamage => _animationDrivenDamage;
        public CombatEntity CommittedAnimationTarget => _animationAttackTarget;
        public float AttackHitNormalizedTime { get; private set; } = 0.6f;
        public IReadOnlyCollection<CombatEntity> BlockedEnemies => _blockedEnemies.Keys;

        public event Action<CombatEntity> BlockReleased;

        public bool IsAlive => Health != null && Health.IsAlive;
        public float CurrentHealth => Health != null ? Health.CurrentHealth : 0f;
        public float MaxHealth => Health != null ? Health.MaxHealth : 0f;
        public Vector3 TargetPosition => transform.position + Vector3.up * 0.5f;

        private Vector2Int[] _rangeOverride;
        private bool _rangeOverrideFromSkill;
        private int _rangeOverrideTicks;

        public bool IsHealer => _config is OperatorConfig operatorConfig && operatorConfig.isHealer;

        public bool CanAttack => IsAlive
            && _config != null
            && _config.normalAttack != null
            && AttackCycle != null;

        public void Initialize(
            CombatWorld world,
            Faction faction,
            UnitConfig config,
            Vector2Int gridPosition = default,
            GridFacing facing = GridFacing.Right)
        {
            if (_world != null)
            {
                _world.Unregister(this);
            }

            // 对象池复用：清掉上一局的索敌、阻挡、攻击和技能临时状态。
            CurrentTarget = null;
            _contactTarget = null;
            _blockedEnemies.Clear();
            UsedBlockWeight = 0;
            _attackTargets.Clear();
            _splashBuffer.Clear();
            _nextAttackMultiplier = 1f;
            _nextAttackExtraHits = 0;
            _timedExtraHits = 0;
            _attacksAllInRange = false;
            _healOnAttackPercent = 0f;
            _animationDrivenDamage = false;
            _projectileDrivenDamage = false;
            _animationAttackTarget = null;
            Skills = null;

            _world = world;
            _faction = faction;
            _config = config;
            GridPosition = gridPosition;
            Facing = facing;

            Health = GetComponent<HealthComponent>();
            if (Health == null)
            {
                Health = gameObject.AddComponent<HealthComponent>();
            }

            Stats = new StatBlock();
            if (config != null)
            {
                Stats.SetBase(StatType.MaxHealth, config.maxHealth);
                Stats.SetBase(StatType.Attack, config.attack);
                Stats.SetBase(StatType.Defense, config.defense);
                Stats.SetBase(StatType.MagicResist, config.magicResist);
                Health.Initialize(config.maxHealth);
                AttackCycle = config.normalAttack != null
                    ? new AttackCycle(config.attackIntervalTicks, config.windupTicks)
                    : null;
            }
            else
            {
                Health.Initialize(100f);
            }

            if (_world != null)
            {
                _world.Register(this);
            }
        }

        public void AttachSkills(SkillComponent skills)
        {
            Skills = skills;
        }

        public void SetGridPosition(Vector2Int cell, GridFacing facing)
        {
            GridPosition = cell;
            Facing = facing;
        }

        public void Tick(int tick)
        {
            if (!IsAlive)
            {
                return;
            }

            Skills?.Tick();

            if (CanAttack)
            {
                AttackCycle.Tick(this);
            }

            // 本 tick 用完之后再递减
            TickTimedModifiers();
            TickRangeOverride();
        }

        private void TickRangeOverride()
        {
            if (_rangeOverrideTicks <= 0)
            {
                return;
            }

            _rangeOverrideTicks--;
            if (_rangeOverrideTicks <= 0)
            {
                _rangeOverride = null;
            }
        }

        private void TickTimedModifiers()
        {
            for (int i = _timedSpeedModifiers.Count - 1; i >= 0; i--)
            {
                _timedSpeedModifiers[i].RemainingTicks--;
                if (_timedSpeedModifiers[i].RemainingTicks <= 0)
                {
                    _timedSpeedModifiers.RemoveAt(i);
                }
            }

            RecalculateAttackSpeed();

            for (int i = _timedModifiers.Count - 1; i >= 0; i--)
            {
                _timedModifiers[i].RemainingTicks--;
                if (_timedModifiers[i].RemainingTicks <= 0)
                {
                    Stats.RemoveModifiersFrom(_timedModifiers[i].Source);
                    _timedModifiers.RemoveAt(i);
                }
            }
        }

        public bool HasValidTarget()
        {
            if (CurrentTarget == null || !CurrentTarget.IsAlive || _world == null)
            {
                return false;
            }

            // 被阻挡时允许敌人在相邻格“接触攻击”阻挡者，但不改变普通攻击范围。
            if (CurrentTarget == _contactTarget)
            {
                return true;
            }

            // 干员可以攻击自己正在阻挡的敌人，即使该敌人位于身后、不在普通攻击范围内。
            if (_blockedEnemies.ContainsKey(CurrentTarget))
            {
                return true;
            }

            if (IsHealer)
            {
                return CurrentTarget.Faction == _faction
                    && CurrentTarget.CurrentHealth < CurrentTarget.MaxHealth
                    && _world.IsTargetInRange(this, CurrentTarget);
            }

            return _world.IsTargetInRange(this, CurrentTarget);
        }

        public void SetTarget(CombatEntity target)
        {
            CurrentTarget = target;
        }

        public void SetContactTarget(CombatEntity target)
        {
            _contactTarget = target;
        }

        public void ClearContactTarget()
        {
            _contactTarget = null;
        }

        public bool TryBlock(CombatEntity enemy)
        {
            if (!IsAlive || enemy == null || !enemy.IsAlive || BlockCapacity <= 0)
            {
                return false;
            }

            if (_blockedEnemies.ContainsKey(enemy))
            {
                return true;
            }

            int weight = Mathf.Max(1, enemy.BlockWeight);
            if (UsedBlockWeight + weight > BlockCapacity)
            {
                return false;
            }

            _blockedEnemies[enemy] = weight;
            UsedBlockWeight += weight;

            // 干员阻挡成功后默认攻击被自己挡住的单位，避免敌人在身后却无法攻击。
            if (Faction == Faction.Ally
                && !IsHealer
                && (CurrentTarget == null || !CurrentTarget.IsAlive))
            {
                SetTarget(enemy);
            }

            return true;
        }

        public CombatEntity GetFirstBlockedEnemy()
        {
            foreach (CombatEntity enemy in _blockedEnemies.Keys)
            {
                if (enemy != null && enemy.IsAlive)
                {
                    return enemy;
                }
            }

            return null;
        }

        public bool IsBlocking(CombatEntity enemy)
        {
            return IsAlive && enemy != null && _blockedEnemies.ContainsKey(enemy);
        }

        public void ReleaseBlock(CombatEntity enemy)
        {
            if (!_blockedEnemies.TryGetValue(enemy, out int weight))
            {
                return;
            }

            _blockedEnemies.Remove(enemy);
            UsedBlockWeight = Mathf.Max(0, UsedBlockWeight - weight);
            BlockReleased?.Invoke(enemy);
        }

        public void ReleaseAllBlocks()
        {
            if (_blockedEnemies.Count == 0)
            {
                return;
            }

            var released = new List<CombatEntity>(_blockedEnemies.Keys);
            _blockedEnemies.Clear();
            UsedBlockWeight = 0;

            // 先移除关系再发事件，避免事件处理中再次查询到旧状态。
            for (int i = 0; i < released.Count; i++)
            {
                BlockReleased?.Invoke(released[i]);
            }
        }

        public void SetNextAttack(SkillConfig skill)
        {
            _nextAttackMultiplier = Mathf.Max(1f, skill.damageMultiplier);
            _nextAttackExtraHits = Mathf.Max(0, skill.extraHits);
        }

        /// <summary>
        /// 让攻击间隔至少覆盖动画时长，避免 Spine 攻击动画被下一次攻击截断。
        /// </summary>
        public void EnsureAttackIntervalAtLeast(int ticks)
        {
            AttackCycle?.EnsureIntervalAtLeast(ticks);
        }

        /// <summary>
        /// 由 Spine 动画命中帧驱动普通攻击伤害，避免 AttackCycle 提前结算。
        /// </summary>
        public void ConfigureAnimationDrivenDamage(bool enabled, float normalizedTime)
        {
            _animationDrivenDamage = enabled;
            if (enabled)
            {
                _projectileDrivenDamage = false;
            }

            AttackHitNormalizedTime = Mathf.Clamp01(normalizedTime);
        }

        /// <summary>
        /// 普通攻击由弹道命中时结算，AttackCycle 不再自动造成伤害。
        /// </summary>
        public void ConfigureProjectileDamage(bool enabled)
        {
            _projectileDrivenDamage = enabled;
            if (enabled)
            {
                _animationDrivenDamage = false;
            }
        }

        public void SetAttackHitNormalizedTime(float normalizedTime)
        {
            AttackHitNormalizedTime = Mathf.Clamp01(normalizedTime);
        }

        /// <summary>
        /// 动画开始播放时锁定本次攻击目标；之后目标离开范围也继续结算。
        /// </summary>
        public void BeginAnimationAttack()
        {
            _animationAttackTarget = CurrentTarget;
        }

        public void ApplyTimedSkill(SkillConfig skill)
        {
            ClearTimedSkill();

            if (skill.attackPercent > 0f)
            {
                Stats.AddModifier(new StatModifier(StatType.Attack, 0f, skill.attackPercent, _skillStatSource));
            }

            _timedExtraHits = Mathf.Max(0, skill.extraHits);
            _attacksAllInRange = skill.attacksAllInRange;
            _healOnAttackPercent = skill.healPercent;

            _skillAttackSpeedPercent = skill.attackSpeedPercent;
            // 攻击间隔缩短：间隔 × (1 - p)，换算成速度倍率 1 / (1 - p)
            if (skill.attackIntervalReducePercent > 0f)
            {
                float factor = Mathf.Clamp(skill.attackIntervalReducePercent, 0f, 0.95f);
                _skillAttackSpeedPercent = 1f / (1f - factor) - 1f;
            }

            _timedMultiTarget = Mathf.Max(0, skill.multiTargetCount);
            _timedProjectileTint = skill.projectileTint;
            // 多目标技能不走普攻动画：按间隔直接发射，避免动画帧还没到就被下一次攻击打断
            SkillVolleyInterval = 0f;
            if (_timedMultiTarget > 1 && _config != null)
            {
                float reduce = Mathf.Clamp(skill.attackIntervalReducePercent, 0f, 0.95f);
                SkillVolleyInterval = _config.attackIntervalTicks / 30f * (1f - reduce);
            }
            if (skill.rangeCells != null && skill.rangeCells.Length > 0)
            {
                _rangeOverride = skill.rangeCells;
                _rangeOverrideTicks = int.MaxValue;
                _rangeOverrideFromSkill = true;
            }

            RecalculateAttackSpeed();
        }

        /// <summary>从当前攻击范围内随机挑最多 count 个敌人，供多目标技能使用。</summary>
        public void GetRandomTargets(int count, List<CombatEntity> results)
        {
            results.Clear();
            if (!IsAlive || _world == null || _world.Space == null || count <= 0)
            {
                return;
            }

            _attackTargets.Clear();
            _world.Space.GetTargetsInRange(this, GetEffectiveRange(), _attackTargets);
            for (int i = _attackTargets.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                CombatEntity temp = _attackTargets[i];
                _attackTargets[i] = _attackTargets[j];
                _attackTargets[j] = temp;
            }

            for (int i = 0; i < _attackTargets.Count && results.Count < count; i++)
            {
                CombatEntity candidate = _attackTargets[i];
                if (candidate != null && candidate.IsAlive)
                {
                    results.Add(candidate);
                }
            }
        }

        public void ClearTimedSkill()
        {
            Stats?.RemoveModifiersFrom(_skillStatSource);
            _timedExtraHits = 0;
            _attacksAllInRange = false;
            _healOnAttackPercent = 0f;
            _timedMultiTarget = 0;
            _timedProjectileTint = Color.white;
            SkillVolleyInterval = 0f;

            if (_rangeOverrideFromSkill)
            {
                _rangeOverride = null;
                _rangeOverrideTicks = 0;
                _rangeOverrideFromSkill = false;
            }

            _skillAttackSpeedPercent = 0f;
            RecalculateAttackSpeed();
        }

        public bool HasEnemyInSkillRange(Vector2Int[] rangeCells)
        {
            if (!IsAlive || _world == null)
            {
                return false;
            }

            _attackTargets.Clear();
            if (_world.Space != null)
            {
                _world.Space.GetTargetsInRange(this, ResolveRange(rangeCells), _attackTargets);
            }
            else if (CurrentTarget != null && CurrentTarget.IsAlive && CurrentTarget.Faction != _faction)
            {
                _attackTargets.Add(CurrentTarget);
            }

            return _attackTargets.Count > 0;
        }

        public bool ExecuteInstantDamage(SkillConfig skill)
        {
            if (!IsAlive || _world == null)
            {
                return false;
            }

            _attackTargets.Clear();
            if (_world.Space != null)
            {
                _world.Space.GetTargetsInRange(this, ResolveRange(skill.rangeCells), _attackTargets);
            }
            else if (CurrentTarget != null && CurrentTarget.IsAlive && CurrentTarget.Faction != _faction)
            {
                _attackTargets.Add(CurrentTarget);
            }

            if (_attackTargets.Count == 0)
            {
                return false;
            }

            float rawDamage = Stats.Get(StatType.Attack) * Mathf.Max(0f, skill.damageMultiplier);
            int hitCount = Mathf.Max(1, skill.hitCount);
            for (int hit = 0; hit < hitCount; hit++)
            {
                for (int i = 0; i < _attackTargets.Count; i++)
                {
                    CombatEntity target = _attackTargets[i];
                    if (target == null || !target.IsAlive)
                    {
                        continue;
                    }

                    var info = new DamageInfo(this, target, skill.damageType, rawDamage);
                    _world.DamagePipeline.Apply(in info);
                }
            }

            return true;
        }

        public void PerformAttack()
        {
            PerformAttackInternal(false, false, null);
        }

        /// <summary>
        /// 只允许 Spine 动画命中帧调用；普通 AttackCycle 结算会被忽略。
        /// </summary>
        public void PerformAttackFromAnimation()
        {
            if (!_animationDrivenDamage)
            {
                return;
            }

            PerformAttackInternal(true, false, null);
        }

        /// <summary>
        /// 弹道命中时调用；目标使用弹道发射时锁定的目标。
        /// </summary>
        public void PerformProjectileAttack(CombatEntity target)
        {
            if (!_projectileDrivenDamage)
            {
                return;
            }

            PerformAttackInternal(false, true, target);
        }

        private void PerformAttackInternal(
            bool fromAnimation,
            bool fromProjectile,
            CombatEntity forcedTarget)
        {
            if (!IsAlive || _world == null || _config == null || _config.normalAttack == null)
            {
                return;
            }

            if ((_animationDrivenDamage && !fromAnimation)
                || (_projectileDrivenDamage && !fromProjectile))
            {
                return;
            }

            CombatEntity target = forcedTarget
                ?? (fromAnimation ? _animationAttackTarget : CurrentTarget);
            if (target == null || !target.IsAlive)
            {
                return;
            }

            if (IsHealer)
            {
                PerformHeal(target);
                Skills?.OnAttackPerformed();
                return;
            }

            AttackData data = _config.normalAttack;
            float attack = Stats.Get(StatType.Attack);
            int hitCount = 1 + _timedExtraHits + _nextAttackExtraHits;
            float multiplier = _nextAttackMultiplier;

            if (_attacksAllInRange && _world.Space != null)
            {
                _attackTargets.Clear();
                _world.Space.GetTargetsInRange(this, _config.rangeCells, _attackTargets);
                for (int hit = 0; hit < hitCount; hit++)
                {
                    for (int i = 0; i < _attackTargets.Count; i++)
                    {
                        ApplyDamage(_attackTargets[i], data, data.ComputeRawDamage(attack) * multiplier);
                    }
                }
            }
            else
            {
                for (int hit = 0; hit < hitCount; hit++)
                {
                    ApplyDamage(target, data, data.ComputeRawDamage(attack) * multiplier);
                }

                if (data.damageShape == DamageShape.Splash && data.splashRadius > 0f && data.maxTargets > 1)
                {
                    _splashBuffer.Clear();
                    _world.Registry.GetEnemiesInRadius(_faction, target.TargetPosition, data.splashRadius, _splashBuffer);

                    int applied = 1;
                    for (int i = 0; i < _splashBuffer.Count && applied < data.maxTargets; i++)
                    {
                        CombatEntity other = _splashBuffer[i];
                        if (other == null || other == target || other == this || !other.IsAlive)
                        {
                            continue;
                        }

                        ApplyDamage(other, data, data.ComputeRawDamage(attack) * multiplier);
                        applied++;
                    }
                }
            }

            _nextAttackMultiplier = 1f;
            _nextAttackExtraHits = 0;
            _animationAttackTarget = null;
            Skills?.OnAttackPerformed();
        }

        private void PerformHeal(CombatEntity target)
        {
            float attack = Stats.Get(StatType.Attack);
            float percent = _healOnAttackPercent > 0f ? _healOnAttackPercent : 1f;
            target.Health.Heal(attack * percent);
        }

        public void TakeDamage(in DamageResult result)
        {
            if (!IsAlive)
            {
                return;
            }

            Skills?.OnDamaged();
            Health.TakeDamage(in result);

            if (!IsAlive)
            {
                CurrentTarget = null;
                _contactTarget = null;
                AttackCycle?.Reset();
                ReleaseAllBlocks();
                ClearTimedSkill();
            }
        }

        public void SetRangeOverride(Vector2Int[] cells, int durationTicks)
        {
            _rangeOverride = cells;
            _rangeOverrideTicks = Mathf.Max(1, durationTicks);
        }

        public Vector2Int[] GetEffectiveRange(Vector2Int[] skillRange = null)
        {
            if (_rangeOverride != null && _rangeOverrideTicks > 0)
                return _rangeOverride;

            if (skillRange != null && skillRange.Length > 0)
                return skillRange;

            return _config != null ? _config.rangeCells : new Vector2Int[0];
        }

        private Vector2Int[] ResolveRange(Vector2Int[] skillRange)
        {
            return GetEffectiveRange(skillRange);
        }

        private void ApplyDamage(CombatEntity target, AttackData data, float rawDamage)
        {
            if (target == null || !target.IsAlive)
            {
                return;
            }

            var info = new DamageInfo(this, target, data.damageType, rawDamage);
            _world.DamagePipeline.Apply(in info);
        }

        private void OnDestroy()
        {
            if (_world != null)
            {
                _world.Unregister(this);
            }
        }

        public void AddTimedAttackSpeed(float percent, int durationTicks)
        {
            _timedSpeedModifiers.Add(new TimedModifier
            {
                Percent = percent,
                RemainingTicks = Mathf.Max(1, durationTicks),
                Source = this,
            });
            RecalculateAttackSpeed();
        }

        public void AddTimedStatModifier(StatType stat, float flat, float percent, int durationTicks)
        {
            var source = new object();
            _timedModifiers.Add(new TimedModifier
            {
                Stat = stat,
                Flat = flat,
                Percent = percent,
                RemainingTicks = Mathf.Max(1, durationTicks),
                Source = source,
            });
            Stats.AddModifier(new StatModifier(stat, flat, percent, source));
        }

        private void RecalculateAttackSpeed()
        {
            float multiplier = 1f + _skillAttackSpeedPercent;
            for (int i = 0; i < _timedSpeedModifiers.Count; i++)
                multiplier *= 1f + _timedSpeedModifiers[i].Percent;

            AttackCycle?.SetSpeedMultiplier(multiplier);
        }
    }
}
