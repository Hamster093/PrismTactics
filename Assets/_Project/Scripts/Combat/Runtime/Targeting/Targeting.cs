/****************************************************
    文件：Targeting.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：索敌策略与索敌服务：最近、最低/最高生命、随机，支持世界距离与格子空间。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 索敌策略接口：从候选列表中选出一个目标。
    /// 职责边界：只负责"从给定候选中挑一个"，不负责范围过滤、存活过滤（由调用方或实现自行决定）。
    /// 注意：不同实现对接的过滤假设不同——Nearest/Lowest/Highest 会自行跳过 null 和死亡，
    ///       Random 不做任何过滤，依赖调用方传入干净候选（事实陈述，未改代码）。
    /// </summary>
    public interface ITargetSelector
    {
        /// <summary>
        /// 从候选列表中选出目标。
        /// 前置条件：candidates 非 null（各实现的处理方式不同，见具体实现）。
        /// 返回 null 表示没有可选目标。
        /// </summary>
        CombatEntity Select(CombatEntity source, IReadOnlyList<CombatEntity> candidates);
    }
    /// <summary>
    /// 最近目标选择器：按世界坐标平方距离选最近。
    /// 注意：不检查距离范围，只做"谁最近"；范围过滤由 TargetingService 在收集候选时完成。
    /// 平方距离比较省去开方，只用于排序，不影响最近语义。
    /// </summary>
    public sealed class NearestTargetSelector : ITargetSelector
    {
        public CombatEntity Select(CombatEntity source, IReadOnlyList<CombatEntity> candidates)
        {
            CombatEntity best = null;
            // 用 float.MaxValue 作初始最小值，保证第一个有效候选一定能选中。
            float bestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                CombatEntity candidate = candidates[i];
                // 跳过 null 和死亡：候选列表可能包含已死亡实体（收集后状态变化）。
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }
                // 平方距离比较：只用于排序，不需要开方。
                float distance = (candidate.TargetPosition - source.TargetPosition).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }
    }
    /// <summary>
    /// 最低生命目标选择器：按当前生命值绝对值选最低（不是生命比例）。
    /// 注意：与治疗索敌的"最低生命比例"不同——这里比的是 CurrentHealth 绝对值，
    ///       一个高血量上限的单位可能比低血量上限的单位当前血量更高，不会被选中。
    /// </summary>
    public sealed class LowestHealthTargetSelector : ITargetSelector
    {
        public CombatEntity Select(CombatEntity source, IReadOnlyList<CombatEntity> candidates)
        {
            CombatEntity best = null;
            float bestHealth = float.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                CombatEntity candidate = candidates[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }
                // 严格小于：并列时保留先遇到的候选，结果稳定
                if (candidate.CurrentHealth < bestHealth)
                {
                    bestHealth = candidate.CurrentHealth;
                    best = candidate;
                }
            }

            return best;
        }
    }
    /// <summary>
    /// 最高生命目标选择器：按当前生命值绝对值选最高（不是生命比例）。
    /// 注意：与 LowestHealth 对称，比较的是 CurrentHealth 绝对值而非比例。
    /// </summary>
    public sealed class HighestHealthTargetSelector : ITargetSelector
    {
        public CombatEntity Select(CombatEntity source, IReadOnlyList<CombatEntity> candidates)
        {
            CombatEntity best = null;
            // 用 float.MinValue 作初始最大值，保证第一个有效候选一定能选中
            float bestHealth = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                CombatEntity candidate = candidates[i];
                if (candidate == null || !candidate.IsAlive)
                {
                    continue;
                }
                // 严格大于：并列时保留先遇到的候选，结果稳定。
                if (candidate.CurrentHealth > bestHealth)
                {
                    bestHealth = candidate.CurrentHealth;
                    best = candidate;
                }
            }

            return best;
        }
    }
    /// <summary>
    /// 随机目标选择器：用 System.Random 按种子随机选一个。
    ///
    /// 设计取舍：
    /// - 用 System.Random 而非 UnityEngine.Random：种子可控、可复现，便于战斗回放/测试；
    /// - 不做 null/死亡过滤：依赖调用方传入干净候选（与 Nearest/Lowest/Highest 不对称，事实陈述，未改代码）；
    /// - 空列表返回 null，避免 Random.Next(0) 抛异常。
    /// </summary>
    public sealed class RandomTargetSelector : ITargetSelector
    {
        /// <summary>
        /// 随机数生成器；种子由构造传入，决定整个战斗的随机序列。
        /// 注意：不是线程安全的，且状态随每次 Select 推进，不可与外部共享。
        /// </summary>
        private readonly System.Random _random;
        /// <summary>
        /// 构造随机选择器；相同种子产生相同序列，便于复现。
        /// </summary>
        public RandomTargetSelector(int seed)
        {
            _random = new System.Random(seed);
        }

        public CombatEntity Select(CombatEntity source, IReadOnlyList<CombatEntity> candidates)
        {
            // 空候选直接返回 null，避免 Random.Next(0) 抛 ArgumentOutOfRangeException。
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }
            // 注意：不做 null/死亡过滤，候选列表必须由调用方保证干净。
            return candidates[_random.Next(candidates.Count)];
        }
    }

    // <summary>
    /// 索敌服务：为所有需要自动索敌的实体批量扫描并设置目标。
    ///
    /// 核心机制：
    ///   ScanAll 遍历注册表，对每个"可攻击 + 自动索敌 + 当前无有效目标"的实体重新选目标；
    ///   普通单位走 Select（敌方），治疗者走 SelectHealTarget（友方最低生命比例）。
    ///
    /// 范围过滤两种模式：
    ///   - 有 ICombatSpace：委托 GetTargetsInRange / GetAlliesInRange 按格子空间过滤；
    ///   - 无 ICombatSpace：按世界坐标平方距离手动收集（CollectByWorldDistance / 内联友方收集）。
    ///
    /// 职责边界：
    /// - 本类只管"选谁"，不负责伤害、移动、动画；
    /// - 目标保持逻辑依赖 CombatEntity.HasValidTarget()，本类不重复实现"目标是否仍有效"的判断；
    /// - 候选列表 _candidates 是共享缓冲，调用方不要持有引用。
    ///
    /// 设计取舍：
    /// - 共享 _candidates 复用列表，避免每次扫描分配新 List；
    /// - 选择器在构造时一次性实例化并缓存，运行时不重复 new；
    /// - 普通单位找不到目标时允许回退到"自己阻挡的敌人"，治疗者不做此回退（不对称，见 ScanAll）。
    /// </summary>
    public sealed class TargetingService
    {
        #region 依赖与状态字段
        /// <summary>战斗实体注册表；ScanAll 和世界距离兜底都从这里取实体列表。</summary>
        private readonly CombatRegistry _registry;
        /// <summary>
        /// 格子空间接口；非 null 时范围过滤走格子空间，null 时走世界距离兜底。
        /// 构造时可选注入，允许纯世界坐标战斗（无格子）场景。
        /// </summary>
        private readonly ICombatSpace _space;
        /// <summary>
        /// 选择器类型 → 选择器实例 的映射；构造时一次性建好，运行时不重复 new。
        /// 注意：RandomTargetSelector 的种子在构造时硬编码为 12345，见构造函数。
        /// </summary>
        private readonly Dictionary<TargetSelectorType, ITargetSelector> _selectors;
        /// <summary>
        /// 共享候选缓冲：SelectHealTarget 和 Select 都会 Clear 后复用。
        /// 注意：调用方不要持有引用，下次调用会被覆盖；不可重入（同一线程内嵌套调用会互相清空）。
        /// </summary>
        private readonly List<CombatEntity> _candidates = new List<CombatEntity>();
        #endregion

        #region 构造
        /// <summary>
        /// 构造索敌服务。
        /// 参数 space 可选：为 null 时退化为纯世界距离索敌。
        /// 副作用：一次性实例化四种选择器并缓存；RandomTargetSelector 种子硬编码为 12345。
        /// 注意：硬编码种子意味着每次新建 TargetingService 的随机序列相同，
        ///       不同战斗实例之间随机结果可复现，但也可能导致多局体验雷同（事实陈述，未改代码）。
        /// </summary>
        public TargetingService(CombatRegistry registry, ICombatSpace space = null)
        {
            _registry = registry;
            _space = space;
            _selectors = new Dictionary<TargetSelectorType, ITargetSelector>
            {
                { TargetSelectorType.Nearest, new NearestTargetSelector() },
                { TargetSelectorType.LowestHealth, new LowestHealthTargetSelector() },
                { TargetSelectorType.HighestHealth, new HighestHealthTargetSelector() },
                // 种子硬编码 12345：保证可复现，但多局随机序列相同。
                { TargetSelectorType.Random, new RandomTargetSelector(12345) }
            };
        }
        #endregion

        #region 公开接口
        /// <summary>
        /// 批量扫描并为需要索敌的实体设置目标。
        ///
        /// 执行顺序：
        /// 1. 遍历注册表所有实体；
        /// 2. 跳过 null、死亡、不可攻击、非自动索敌、已有有效目标的实体；
        /// 3. 治疗者走 SelectHealTarget，普通单位走 Select；
        /// 4. 普通单位范围找不到目标时，回退到"自己阻挡的敌人"；
        /// 5. 写入目标。
        ///
        /// 前置条件：由外部按固定频率调用（注释声称每 3 个逻辑 tick 一次，但本方法内无节流逻辑，
        ///           实际频率由调用方决定——事实陈述，未改代码）。
        /// 副作用：修改实体的目标。
        /// 注意：已存在有效目标的实体被跳过，保持目标不换；HasValidTarget 的具体语义由 CombatEntity 决定。
        /// </summary>
        public void ScanAll()
        {
            IReadOnlyList<CombatEntity> entities = _registry.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                CombatEntity source = entities[i];
                // 1. 跳过不需要重新索敌的实体：空、死、不能攻击、非自动索敌、已有有效目标。
                // 注意：HasValidTarget 是否包含范围检查取决于 CombatEntity 实现，本类不重复判断。
                if (source == null
                    || !source.IsAlive
                    || !source.CanAttack
                    || !source.AutoTargeting
                    || source.HasValidTarget())
                {
                    continue;
                }
                // 2. 治疗者走友方最低生命比例，普通单位走敌方选择器。
                CombatEntity target = source.IsHealer ? SelectHealTarget(source) : Select(source);
                // 3. 普通单位兜底：范围过滤找不到目标时，允许攻击自己阻挡的敌人（例如敌人在身后）。
                // 治疗者不做此兜底（不对称逻辑，事实陈述，未改代码）。
                if (target == null && !source.IsHealer)
                {
                    // 普通范围找不到目标时，允许攻击自己阻挡的敌人（例如敌人在身后）。
                    target = source.GetFirstBlockedEnemy();
                }
                // 4. 写入目标；target 为 null 时等于清空目标。
                source.SetTarget(target);
            }
        }
       
        /// <summary>
        /// 为单个普通单位选择攻击目标。
        ///
        /// 执行顺序：
        /// 1. 前置检查：source 非 null、存活、有配置；
        /// 2. 清空共享候选缓冲；
        /// 3. 有格子空间则走 GetTargetsInRange，否则按世界距离收集；
        /// 4. 候选为空返回 null；
        /// 5. 按配置的 targetSelector 取选择器，配置缺失时回退 Nearest。
        ///
        /// 副作用：会清空并填充共享 _candidates；不修改 source。
        /// 注意：返回的是选择器结果，未做"阻挡敌人"兜底——兜底在 ScanAll 中。
        /// </summary>
        public CombatEntity Select(CombatEntity source)
        {
            // 1. 前置检查：空、死、无配置都返回 null。
            if (source == null || !source.IsAlive || source.Config == null)
            {
                return null;
            }
            // 2. 复用共享缓冲，先清空。
            _candidates.Clear();
            // 3. 有格子空间走格子范围过滤，否则走世界距离兜底。
            if (_space != null)
            {
                _space.GetTargetsInRange(source, _candidates);
            }
            else
            {
                CollectByWorldDistance(source);
            }
            // 4. 候选为空直接返回 null。
            if (_candidates.Count == 0)
            {
                return null;
            }
            // 5. 配置的选择器缺失时回退 Nearest，避免配置错误导致不索敌。
            if (!_selectors.TryGetValue(source.Config.targetSelector, out ITargetSelector selector))
            {
                selector = _selectors[TargetSelectorType.Nearest];
            }

            return selector.Select(source, _candidates);
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 为治疗者选择友方目标：按"当前生命 / 最大生命"比例最低选。
        ///
        /// 执行顺序：
        /// 1. 清空共享候选缓冲；
        /// 2. 有格子空间走 GetAlliesInRange，否则内联按世界距离收集友方；
        /// 3. 遍历候选，跳过空/死/最大生命 <= 0 的实体，选生命比例最低。
        ///
        /// 注意：与 Select 的不对称——
        ///   - 治疗者走友方，普通单位走敌方；
        ///   - 治疗者比的是生命比例，普通单位比的是绝对值（LowestHealth 选择器）；
        ///   - 治疗者不做"阻挡敌人"兜底；
        ///   - 本方法未检查 source.Config 为 null，直接访问 source.Config.rangeCells 和 attackRange，
        ///     依赖 ScanAll 的前置检查（source.CanAttack 通常隐含 Config 非空）——事实陈述，未改代码。
        /// 副作用：会清空并填充共享 _candidates；不修改 source。
        /// </summary>
        private CombatEntity SelectHealTarget(CombatEntity source)
        {
            // 1. 复用共享缓冲，先清空。
            _candidates.Clear();
            // 2. 有格子空间走格子范围过滤，否则按世界距离收集友方（与 Select 对称但查友方）。
            if (_space != null)
            {
                _space.GetAlliesInRange(source, source.Config.rangeCells, _candidates);
            }
            else
            {
                IReadOnlyList<CombatEntity> entities = _registry.Entities;
                for (int i = 0; i < entities.Count; i++)
                {
                    CombatEntity candidate = entities[i];
                    // 跳过空、死、非同阵营、满血友方；满血不需要治疗。
                    if (candidate == null
                        || !candidate.IsAlive
                        || candidate.Faction != source.Faction
                        || candidate.CurrentHealth >= candidate.MaxHealth)
                    {
                        continue;
                    }
                    // 世界距离兜底：用 attackRange 而非 rangeCells 做范围判断。
                    // 注意：这里用 attackRange（世界距离）对应格子空间的 rangeCells（格子数），两者语义不同，
                    //       但都是各自模式下的"攻击范围"配置（事实陈述，未改代码）。
                    if ((candidate.TargetPosition - source.TargetPosition).sqrMagnitude
                        <= source.Config.attackRange * source.Config.attackRange)
                    {
                        _candidates.Add(candidate);
                    }
                }
            }
            // 3. 按生命比例最低选；比例相同保留先遇到的候选。
            CombatEntity best = null;
            float bestRatio = float.MaxValue;
            for (int i = 0; i < _candidates.Count; i++)
            {
                CombatEntity candidate = _candidates[i];
                // 二次过滤：候选可能在收集后状态变化；MaxHealth <= 0 防除零。
                if (candidate == null || !candidate.IsAlive || candidate.MaxHealth <= 0f)
                {
                    continue;
                }

                float ratio = candidate.CurrentHealth / candidate.MaxHealth;
                if (ratio < bestRatio)
                {
                    bestRatio = ratio;
                    best = candidate;
                }
            }

            return best;
        }
        /// <summary>
        /// 无格子空间时的世界距离兜底：收集攻击范围内、非同阵营、非自身的存活敌人。
        /// 副作用：清空并填充共享 _candidates。
        /// 注意：排除自身（candidate == source），但 SelectHealTarget 的格子空间分支未显式排除自身，
        ///       依赖 GetAlliesInRange 内部是否排除（事实陈述，未改代码）。
        /// </summary>
        private void CollectByWorldDistance(CombatEntity source)
        {
            _candidates.Clear();
            // 平方距离比较，省去开方。
            float rangeSquared = source.Config.attackRange * source.Config.attackRange;
            IReadOnlyList<CombatEntity> entities = _registry.Entities;

            for (int i = 0; i < entities.Count; i++)
            {
                CombatEntity candidate = entities[i];
                // 跳过空、死、自身。
                if (candidate == null || !candidate.IsAlive || candidate == source)
                {
                    continue;
                }
                // 跳过同阵营：普通索敌只找敌人。
                if (candidate.Faction == source.Faction)
                {
                    continue;
                }
                // 超出攻击范围跳过。
                if ((candidate.TargetPosition - source.TargetPosition).sqrMagnitude > rangeSquared)
                {
                    continue;
                }

                _candidates.Add(candidate);
            }
        }
        #endregion
    }
}
