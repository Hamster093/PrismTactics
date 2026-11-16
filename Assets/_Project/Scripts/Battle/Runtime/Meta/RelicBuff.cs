/****************************************************
    文件：RelicBuff.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 18:30:00
	功能：汇总本次运行已获得藏品的数值加成，并应用到干员配置。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class RelicBuff
    {
        public float AttackPercent { get; private set; }
        public float HealthPercent { get; private set; }
        public float DefensePercent { get; private set; }
        public float AttackSpeedPercent { get; private set; }
        public int RelicCount { get; private set; }

        public static RelicBuff FromSession(RelicLibrary library)
        {
            var buff = new RelicBuff();
            if (library == null || library.relics == null)
            {
                return buff;
            }

            for (int i = 0; i < library.relics.Count; i++)
            {
                RelicDefinition relic = library.relics[i];
                if (relic == null || !RunSession.HasRelic(relic.id))
                {
                    continue;
                }

                buff.AttackPercent += relic.attackPercent;
                buff.HealthPercent += relic.healthPercent;
                buff.DefensePercent += relic.defensePercent;
                buff.AttackSpeedPercent += relic.attackSpeedPercent;
                buff.RelicCount++;
            }

            return buff;
        }

        /// <summary>
        /// 在创建运行时 OperatorConfig 后、CombatEntity 初始化前应用。
        /// </summary>
        public void Apply(OperatorConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.maxHealth *= 1f + HealthPercent;
            config.attack *= 1f + AttackPercent;
            config.defense *= 1f + DefensePercent;

            if (AttackSpeedPercent > 0f)
            {
                // 攻速提升表现为攻击间隔缩短；至少保留 1 tick。
                config.attackIntervalTicks = Mathf.Max(
                    1,
                    Mathf.RoundToInt(
                        config.attackIntervalTicks / (1f + AttackSpeedPercent)));
            }
        }
    }
}
