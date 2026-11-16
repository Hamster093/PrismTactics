/****************************************************
    文件：StatBlock.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：属性块：基础值、固定/百分比修正与按来源移除。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 单条属性修正：对某个 StatType 施加固定值（Flat）和百分比（Percent），并记录来源（Source）用于批量移除。
    ///
    /// 设计取舍：
    /// - 用 readonly struct：修正条目量大、生命周期短，struct 避免堆分配；readonly 保证传入后不被意外改写；
    /// - Flat 和 Percent 分开存：计算顺序固定为 (基础值 + Flat) * (1 + Percent)，两者语义不同不可合并；
    /// - Source 用 object：不依赖具体类型（技能、buff、遗物、装备都可能是来源），由调用方保证引用唯一；
    ///   移除时按引用相等匹配（见 StatBlock.RemoveModifiersFrom），所以同一来源必须传同一个对象实例。
    ///
    /// 坑点：
    /// - Source 为 null 的修正无法通过 RemoveModifiersFrom 移除（该方法对 null 直接返回），
    ///   等于"永久修正"；如果不想让它永久生效，必须给一个非 null 的来源对象；
    /// - default(StatModifier) 是一个全零修正（Stat 为枚举默认值、Flat/Percent 为 0、Source 为 null），
    ///   AddModifier 会触发 Changed 但数值不变，属于无意义修正，调用方应避免传入 default。
    /// </summary>
    public readonly struct StatModifier
    {
        public readonly StatType Stat;// <summary>作用的目标属性类型。</summary>
        public readonly float Flat;// <summary>固定值修正：直接加到基础值上，不受百分比影响。</summary>
        public readonly float Percent; //百分比修正：以小数形式表示（0.1 = +10%），多个百分比修正** 加算**而非连乘，
        public readonly object Source;// 修正来源：任意对象引用，用于 RemoveModifiersFrom 按引用批量移除。
        /// <summary>
        /// 构造一条修正。
        /// 默认参数允许只传 Stat 得到零修正，但这种零修正没有意义且会触发 Changed，调用方应避免。
        /// </summary>
        public StatModifier(StatType stat, float flat = 0f, float percent = 0f, object source = null)
        {
            Stat = stat;
            Flat = flat;
            Percent = percent;
            Source = source;
        }
    }

    /// <summary>
    /// 属性块：基础值叠加固定值与百分比修正，计算顺序固定为：
    /// (基础值 + 固定值) * (1 + 百分比)。
    ///
    /// 核心机制：
    ///   - 基础值存在 _baseValues 字典，每个 StatType 一个值；
    ///   - 修正存在 _modifiers 列表，每条记录目标 Stat / Flat / Percent / Source；
    ///   - Get 时遍历 _modifiers，把匹配当前 Stat 的 Flat 和 Percent 分别累加，再套公式计算。
    ///
    /// 职责边界：
    /// - 本类只管"基础值 + 修正的数值计算"，不关心修正从哪来、什么时机加；
    /// - 修正的添加/移除由外部系统（技能、buff、装备）调用；
    /// - 本类不做缓存，每次 Get 都实时遍历计算，数值正确性优先于性能。
    ///
    /// 设计取舍：
    /// - 百分比加算而非连乘：多个 +10% 得到 +20%，结果可预测、便于数值平衡；
    ///   连乘会让修正顺序影响结果，且数值膨胀更快；
    /// - 基础值和修正分离存储：基础值可单独读写，修正按来源批量移除，职责清晰；
    /// - Get 无缓存：修正随时可能增删，缓存失效逻辑复杂，且属性查询频率通常不高，实时计算可接受；
    ///   若未来属性被每帧高频读取，可考虑加 dirty 标记 + 缓存，但要注意 Changed 事件的触发时机。
    /// </summary>
    public sealed class StatBlock
    {
        #region 状态字段与事件
        /// <summary>
        /// 基础值字典：StatType → 基础数值。
        /// 注意：未设置的 Stat 在 GetBase 中返回 0，不区分"未设置"和"显式设为 0"。
        /// </summary>
        private readonly Dictionary<StatType, float> _baseValues = new Dictionary<StatType, float>();
        /// <summary>
        /// 修正列表：所有来源、所有属性的修正混在一起，Get 时按 Stat 过滤。
        /// 注意：用 List 而非 Dictionary 分组，因为修正量通常很少，线性遍历比维护分组字典更简单；
        ///       但 Get 的复杂度是 O(全部修正数)，不是 O(当前 Stat 的修正数)。
        /// </summary>
        private readonly List<StatModifier> _modifiers = new List<StatModifier>();
        /// <summary>
        /// 属性变化事件：SetBase / AddModifier / RemoveModifiersFrom 都会触发，参数是变化的 StatType。
        /// 注意：订阅方需在销毁时取消订阅，本类不管理外部订阅生命周期；
        ///       同一 Stat 的多次变化会触发多次事件，订阅方需自行处理重复刷新。
        /// </summary>
        public event Action<StatType> Changed;
        #endregion

        #region 基础值读写
        /// <summary>
        /// 设置基础值：用 Mathf.Max(0f, value) 兜底非负，基础值不允许为负。
        /// 副作用：写入 _baseValues 并触发 Changed，即使新旧值相同也会触发（不做相等判断）。
        /// </summary>
        public void SetBase(StatType stat, float value)
        {
            _baseValues[stat] = Mathf.Max(0f, value);
            Changed?.Invoke(stat);
        }
        /// <summary>
        /// 读取基础值：未设置的 Stat 返回 0。
        /// 注意：返回的是基础值，不含任何修正；要拿最终值用 Get。
        /// </summary>
        public float GetBase(StatType stat)
        {
            return _baseValues.TryGetValue(stat, out float value) ? value : 0f;
        }
        #endregion

        #region 修正管理
        /// <summary>
        /// 添加一条修正。
        /// 副作用：追加到 _modifiers 并触发 Changed。
        /// 注意：不做去重，同一来源重复添加会产生多条修正、重复生效；
        ///       用 in 参数避免 struct 拷贝进方法，但存入 List 时仍会拷贝一次（List 存值类型）。
        /// </summary>
        public void AddModifier(in StatModifier modifier)
        {
            _modifiers.Add(modifier);
            Changed?.Invoke(modifier.Stat);
        }
        /// <summary>
        /// 移除所有来源等于 source 的修正。
        ///
        /// 执行顺序：
        /// 1. source 为 null 直接返回（见下方坑点）；
        /// 2. 倒序遍历 _modifiers，用 ReferenceEquals 匹配 Source；
        /// 3. 命中则记录 Stat、移除该条、触发 Changed。
        ///
        /// 坑点：
        /// - source 为 null 直接返回，意味着 Source 为 null 的修正**永远无法通过此方法移除**，
        ///   只能靠重建 StatBlock 或未来加其他清理接口；所以添加修正时尽量给非 null 来源；
        /// - 用 ReferenceEquals 而非 ==：Source 是 object，== 可能被重载（比如 Unity Object 的 == 有特殊语义），
        ///   ReferenceEquals 保证严格按引用匹配，语义明确；
        /// - 倒序遍历是删除列表元素的正确写法：正序 RemoveAt 会导致后续元素前移、索引错位、漏删；
        /// - 每移除一条触发一次 Changed，同一 Stat 有多条同来源修正时会触发多次，订阅方需容忍重复；
        /// - 遍历中触发 Changed，若订阅方在回调里又调 AddModifier/RemoveModifiersFrom，会修改正在遍历的列表，
        ///   存在重入风险（事实陈述，未改代码）。
        /// </summary>
        public void RemoveModifiersFrom(object source)
        {
            if (source == null)
            {
                return;
            }

            for (int i = _modifiers.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_modifiers[i].Source, source))
                {
                    continue;
                }

                StatType stat = _modifiers[i].Stat;
                _modifiers.RemoveAt(i);
                Changed?.Invoke(stat);
            }
        }
        #endregion

        #region 属性查询
        /// <summary>
        /// 计算属性的最终值，公式固定为：(基础值 + 所有 Flat 之和) * (1 + 所有 Percent 之和)。
        ///
        /// 执行顺序：
        /// 1. 遍历全部修正，累加匹配当前 Stat 的 Flat 和 Percent；
        /// 2. 套公式计算；
        /// 3. 用 Mathf.Max(0f, ...) 兜底非负，防止负修正把结果压到负数。
        ///
        /// 坑点：
        /// - 遍历的是**全部修正**，不是当前 Stat 的修正，复杂度 O(全部修正数)；
        ///   修正多时每次 Get 都有线性开销，且无缓存；
        /// - 百分比是**加算**：多个 +10% 得到 +20%，不是连乘；
        /// - 最终结果下限为 0：负修正再多也不会得到负属性值；
        /// - 本方法无副作用、不触发 Changed，纯查询。
        /// </summary>
        public float Get(StatType stat)
        {
            float flat = 0f;
            float percent = 0f;

            for (int i = 0; i < _modifiers.Count; i++)
            {
                StatModifier modifier = _modifiers[i];
                if (modifier.Stat != stat)
                {
                    continue;
                }

                flat += modifier.Flat;
                percent += modifier.Percent;
            }

            return Mathf.Max(0f, (GetBase(stat) + flat) * (1f + percent));
        }
        #endregion

    }
}
