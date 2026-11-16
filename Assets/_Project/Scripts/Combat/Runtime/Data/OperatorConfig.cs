/****************************************************
    文件：OperatorConfig.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：干员配置：部署费用、阻挡数量、部署地形、治疗职业与技能列表。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    // 干员配置资产：在通用单位配置上扩展部署相关字段
    [CreateAssetMenu(fileName = "OperatorConfig", menuName = "PrismTactics/战斗/干员配置")]
    public sealed class OperatorConfig : UnitConfig
    {
        [Header("干员")]
        public string operatorId = "guard";// 干员类型标识
        public string displayName = "禁卫";// 显示名称
        [Min(0)] public int deployCost = 5;// 部署费用
        [Range(0f, 1f)] public float attackHitNormalizedTime = 0.6f;// 普通攻击动画命中比例
        public bool useProjectile;// 普通攻击是否生成追踪弹道
        [Min(0.1f)] public float projectileSpeed = 4f;// 弹道飞行速度
        public bool canDeployOnHighGround = true;// 是否可部署在高台
        public bool requiresHighGround;// 是否只能部署在高台
        public bool isHealer;// 是否为治疗职业
        public SkillConfig[] skills = new SkillConfig[0];// 可携带技能
        public GridFacing defaultFacing = GridFacing.Right;// 默认朝向
    }
}
