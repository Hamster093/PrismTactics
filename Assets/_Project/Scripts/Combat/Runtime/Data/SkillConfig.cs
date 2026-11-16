/****************************************************
    文件：SkillConfig.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 00:20:00
	功能：技能配置：SP 回复、触发方式、持续时间和技能效果参数。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    [CreateAssetMenu(fileName = "SkillConfig", menuName = "PrismTactics/战斗/技能配置")]
    public sealed class SkillConfig : ScriptableObject
    {
        [Header("基础信息")]
        public string skillId = "skill";
        public string displayName = "技能";
        public string description = string.Empty;
        public SkillActivationType activationType = SkillActivationType.Manual;
        public SpRecoveryType spRecoveryType = SpRecoveryType.Auto;

        [Header("SP")]
        [Min(0)] public int spCost = 10;
        [Min(0)] public int initialSp;
        [Min(0f)] public float spPerSecond = 1f;

        [Header("效果")]
        public SkillEffectType effectType = SkillEffectType.NextAttack;
        [Min(0)] public int durationTicks;
        public DamageType damageType = DamageType.Physical;
        [Min(0f)] public float damageMultiplier = 1f;
        [Min(1)] public int hitCount = 1;
        [Min(0)] public int extraHits;
        [Range(0f, 5f)] public float attackPercent;
        [Range(0f, 5f)] public float attackSpeedPercent;
        /// <summary>攻击间隔缩短百分比（0.65 表示间隔变成原来的 35%）。</summary>
        [Range(0f, 0.95f)] public float attackIntervalReducePercent;
        /// <summary>持续期间每次攻击的目标数；大于 1 时随机挑范围内的多个敌人。</summary>
        [Min(0)] public int multiTargetCount;
        /// <summary>弹道染色，默认白色表示不染色。</summary>
        public Color projectileTint = Color.white;
        [Range(0f, 5f)] public float healPercent;
        public bool attacksAllInRange;
        public bool requiresEnemy;
        public Vector2Int[] rangeCells = new Vector2Int[0];
        public string effectKey = string.Empty;

        public int MaxSp => Mathf.Max(1, spCost);
    }
}
