/****************************************************
    文件：CombatEnums.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗枚举：阵营、伤害类型、攻击形态、状态、索敌、属性与格子朝向。
*****************************************************/

namespace PrismTactics.Combat
{// 阵营
    public enum Faction
    {
        Ally,// 友方
        Enemy,// 敌方
        Neutral// 中立
    }
    // 伤害类型
    public enum DamageType
    {
        Physical, // 物理
        Arts,// 法术
        True// 真实伤害（无视防御/抗性）
    }
    // 攻击形态
    public enum DamageShape
    {
        Single,// 单体
        Splash // 溅射
    }
    // 攻击循环状态
    public enum AttackCycleState
    {
        Ready,// 就绪
        Windup, // 前摇
        Recovery // 后摇
    }
    // 索敌策略
    public enum TargetSelectorType
    {
        Nearest,// 最近
        LowestHealth,// 血量最低
        HighestHealth,// 血量最高
        Random// 随机
    }
    // 属性类型
    public enum StatType
    {
        MaxHealth, // 最大生命
        Attack, // 攻击
        Defense,// 防御
        MagicResist// 法术抗性
    }

    /// <summary>
    /// 格子朝向。Right 为基准方向，其余方向按逆时针旋转。
    /// </summary>
    public enum GridFacing
    {
        Right,  // 右
        Up,     // 上
        Left,   // 左
        Down    // 下
    }

    // 技能触发方式
    public enum SkillActivationType
    {
        Manual, // 手动
        Auto    // 自动
    }

    // SP 回复方式
    public enum SpRecoveryType
    {
        Auto,   // 自动回复
        Attack, // 攻击回复
        Defend  // 受击回复
    }

    // 技能效果类型
    public enum SkillEffectType
    {
        NextAttack,   // 强化下一次攻击
        TimedBuff,    // 持续型自身 Buff
        InstantDamage // 瞬间范围伤害
    }

    // 技能运行时状态
    public enum SkillState
    {
        Charging, // 充能中
        Ready,    // 已就绪，等待手动释放
        Active    // 持续技能生效中
    }
}
