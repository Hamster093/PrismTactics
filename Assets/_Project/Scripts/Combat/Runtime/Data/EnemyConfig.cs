/****************************************************
    文件：EnemyConfig.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：敌人配置：移动速度、阻挡权重与漏怪扣血。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    // 敌人配置资产：在通用单位配置上扩展移动与漏怪参数
    [CreateAssetMenu(fileName = "EnemyConfig", menuName = "PrismTactics/战斗/敌人配置")]
    public sealed class EnemyConfig : UnitConfig
    {
        [Header("敌人")]
        public int enemyId = 1;// 敌人类型数字编号
        public string displayName = "普通敌人";// 显示名称
        [Min(0.1f)] public float moveSpeed = 1.5f;// 移动速度（格/秒）
        [Min(1)] public int blockWeight = 1;// 占用阻挡位的权重
        [Min(0)] public int lifeDamage = 1;// 漏怪时扣除的生命值
    }
}
