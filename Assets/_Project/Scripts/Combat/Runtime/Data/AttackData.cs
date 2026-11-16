/****************************************************
    文件：AttackData.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：攻击数据配置：伤害类型、攻击形态、倍率与溅射参数。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    // 攻击数据资产：描述一次攻击的伤害类型、形态与倍率
    [CreateAssetMenu(fileName = "AttackData", menuName = "PrismTactics/战斗/攻击数据")]
    public sealed class AttackData : ScriptableObject
    {
        public DamageType damageType = DamageType.Physical;// 伤害类型
        public DamageShape damageShape = DamageShape.Single;// 攻击形态（单体/溅射）

        [Min(0f)] public float baseDamage;// 基础固定伤害
        [Min(0f)] public float attackScale = 1f;// 攻击力倍率
        [Min(0f)] public float splashRadius;// 溅射半径（仅 Splash 生效）
        [Min(1)] public int maxTargets = 1;// 最多命中目标数（含主目标）
        // 计算原始伤害 = 基础伤害 + 攻击力 × 倍率，结果夹到非负
        public float ComputeRawDamage(float attack)
        {
            return Mathf.Max(0f, baseDamage + attack * attackScale);
        }
    }
}
