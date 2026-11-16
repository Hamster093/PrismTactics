/****************************************************
    文件：UnitConfig.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：单位通用配置：生命、攻击、防御、索敌、攻击数据与阻挡数量。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 干员和敌人共用的单位配置基类。
    /// 具体单位只扩展自己的身份、部署或移动字段。
    /// </summary>
    public abstract class UnitConfig : ScriptableObject
    {
        [Header("基础属性")]
        [Min(1f)] public float maxHealth = 100f;// 最大生命
        [Min(0f)] public float attack = 20f;// 攻击力
        [Min(0f)] public float defense;// 防御（物理减伤，固定值）
        [Range(0f, 100f)] public float magicResist;// 法术抗性（百分比减伤）

        [Header("攻击")]
        [Min(1)] public int attackIntervalTicks = 30;// 攻击间隔（tick）
        [Min(0)] public int windupTicks = 9;// 前摇时长（tick）
        public TargetSelectorType targetSelector = TargetSelectorType.Nearest;// 索敌策略
        public AttackData normalAttack;// 普通攻击数据

        [Header("阻挡")]
        [Min(0)] public int blockCount;// 可阻挡敌人数量

        [Header("范围")]
        [Min(0.1f)] public float attackRange = 3f;// 世界距离攻击范围（无格子时的回退判定）
        public Vector2Int[] rangeCells = new Vector2Int[0];// 格子相对攻击范围（有格子时使用）

        [Header("调试")]
        public Color debugColor = Color.cyan;// 编辑器调试绘制颜色
    }
}
