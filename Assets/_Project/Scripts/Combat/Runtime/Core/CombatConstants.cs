/****************************************************
    文件：CombatConstants.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗核心常量：tick 频率、索敌间隔与最小伤害比例。
*****************************************************/

namespace PrismTactics.Combat
{
    public static class CombatConstants
    {
        public const int TickRate = 30;//战斗逻辑固定 30Hz
        public const float TickSeconds = 1f / TickRate;//一拍等于多少秒
        public const int TargetScanIntervalTicks = 3;//每隔 3 个 tick扫描一次周围有没有敌人
        public const float MinimumDamageRatio = 0.05f;//最低伤害保底 = 5%
        public const int MaxTicksPerFrame = 4;//一帧最多补 4 个 tick
    }
}
