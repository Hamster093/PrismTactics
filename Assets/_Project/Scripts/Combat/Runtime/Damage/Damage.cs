/****************************************************
    文件：Damage.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：伤害数据结构、减伤公式与前置/后置修正管线。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    // 伤害输入：伤害来源、目标、类型与原始数值
    public readonly struct DamageInfo
    {
        public readonly object Source;// 伤害来源（通常是攻击者，可为 null）
        public readonly IDamageable Target;// 承伤目标
        public readonly DamageType Type; // 伤害类型
        public readonly float Amount;// 原始伤害（构造时已夹到 >= 0）
        public readonly bool IsCritical; // 是否暴击
        // 构造：Amount 自动夹到非负
        public DamageInfo(object source, IDamageable target, DamageType type, float amount, bool isCritical = false)
        {
            Source = source;
            Target = target;
            Type = type;
            Amount = Mathf.Max(0f, amount);
            IsCritical = isCritical;
        }
    }
    // 伤害输出：管线结算后的结果
    public readonly struct DamageResult
    {
        public readonly DamageType Type;// 伤害类型
        public readonly float RawAmount;// 结算前的原始伤害
        public readonly float FinalAmount;// 结算后的最终伤害（构造时已夹到 >= 0）
        public readonly bool IsCritical;//是否暴击
        public readonly bool WasLethal;// 本次是否致命（结算时目标生命 <= 最终伤害）
        // 构造：FinalAmount 自动夹到非负
        public DamageResult(DamageType type, float rawAmount, float finalAmount, bool isCritical, bool wasLethal)
        {
            Type = type;
            RawAmount = rawAmount;
            FinalAmount = Mathf.Max(0f, finalAmount);
            IsCritical = isCritical;
            WasLethal = wasLethal;
        }
    }
    // 伤害修正接口：输入伤害信息与当前数值，返回修正后数值
    public interface IDamageModifier
    {
        float Modify(in DamageInfo info, float amount);
    }
    // 减伤公式集合
    public static class DamageMath
    {
        // 按伤害类型套用减伤：真实伤害不减免，物理减固定防御，法术按抗性百分比
        public static float Mitigate(float amount, DamageType type, float defense, float magicResist)
        {
            if (amount <= 0f)
            {
                return 0f;
            }

            switch (type)
            {
                case DamageType.True:
                    return amount;

                case DamageType.Physical:
                    return Mathf.Max(
                        amount - Mathf.Max(0f, defense),
                        amount * CombatConstants.MinimumDamageRatio);

                case DamageType.Arts:
                {
                    // 抗性夹到 [0, 100]，100 时仍保底 MinimumDamageRatio
                    float resistance = Mathf.Clamp(magicResist, 0f, 100f);
                    return Mathf.Max(
                        amount * (1f - resistance / 100f),
                        amount * CombatConstants.MinimumDamageRatio);
                }

                default:
                    return amount;
            }
        }
    }

    /// <summary>
    /// 前置修正改变输出伤害，随后按目标防御/法抗减伤，
    /// 最后后置修正改变最终结果（护盾、易伤等）。
    /// </summary>
    public sealed class DamagePipeline
    {   // 前置修正（作用于减伤之前，如增伤、暴击）
        private readonly List<IDamageModifier> _preModifiers = new List<IDamageModifier>();
        // 后置修正（作用于减伤之后，如护盾、易伤）
        private readonly List<IDamageModifier> _postModifiers = new List<IDamageModifier>();
        // 添加前置修正
        public void AddPreModifier(IDamageModifier modifier)
        {
            if (modifier != null)
            {
                _preModifiers.Add(modifier);
            }
        }
        // 添加后置修正
        public void AddPostModifier(IDamageModifier modifier)
        {
            if (modifier != null)
            {
                _postModifiers.Add(modifier);
            }
        }
        // 移除前置修正
        public void RemovePreModifier(IDamageModifier modifier)
        {
            _preModifiers.Remove(modifier);
        }
        // 移除后置修正
        public void RemovePostModifier(IDamageModifier modifier)
        {
            _postModifiers.Remove(modifier);
        }
        // 结算伤害：前置修正 -> 减伤 -> 后置修正 -> 判定致命，不直接施加
        public DamageResult Resolve(in DamageInfo info)
        {
            float amount = Mathf.Max(0f, info.Amount);
            // 前置修正，逐个夹到非负
            for (int i = 0; i < _preModifiers.Count; i++)
            {
                amount = Mathf.Max(0f, _preModifiers[i].Modify(info, amount));
            }
            // 取目标防御/法抗（目标为 null 时按 0）
            float defense = info.Target != null ? info.Target.Stats.Get(StatType.Defense) : 0f;
            float resistance = info.Target != null ? info.Target.Stats.Get(StatType.MagicResist) : 0f;
            amount = DamageMath.Mitigate(amount, info.Type, defense, resistance);
            // 后置修正，逐个夹到非负
            for (int i = 0; i < _postModifiers.Count; i++)
            {
                amount = Mathf.Max(0f, _postModifiers[i].Modify(info, amount));
            }
            // 致命判定：目标仍存活且最终伤害不低于当前生命
            bool lethal = info.Target != null
                && info.Target.IsAlive
                && amount >= info.Target.CurrentHealth;

            return new DamageResult(info.Type, info.Amount, amount, info.IsCritical, lethal);
        }
        // 结算并施加伤害：仅当最终伤害 > 0 时才调用目标 TakeDamage
        public DamageResult Apply(in DamageInfo info)
        {
            DamageResult result = Resolve(info);
            if (info.Target != null && result.FinalAmount > 0f)
            {
                info.Target.TakeDamage(in result);
            }

            return result;
        }
    }
}
