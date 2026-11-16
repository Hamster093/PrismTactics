/****************************************************
    文件：FrostNovaHandler.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/4 00:00:00
    功能：霜星技能「冰霜新星」：范围法术伤害 + 3 跳持续伤害。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.HotUpdate
{
    public sealed class FrostNovaHandler : ISkillEffectHandler
    {
        public string EffectKey => "frost_nova";

        public bool Execute(ISkillEffectContext context)
        {
            if (context == null || context.Owner == null)
            {
                return false;
            }

            CombatEntity owner = context.Owner;
            Vector3 center = owner.TargetPosition;
            float attack = owner.Stats.Get(StatType.Attack);

            var targets = context.EnemiesInRadius(center, 3f);
            Debug.Log($"[FrostNova] 命中 {targets.Count} 个敌人");

            for (int i = 0; i < targets.Count; i++)
            {
                context.DealDamage(
                    targets[i],
                    attack * context.Skill.damageMultiplier,
                    DamageType.Arts);
            }

            context.ScheduleRepeating(30, 3, () =>
            {
                var dotTargets = context.EnemiesInRadius(center, 3f);
                for (int i = 0; i < dotTargets.Count; i++)
                {
                    context.DealDamage(dotTargets[i], attack * 0.5f, DamageType.Arts);
                }
            });

            return true;
        }
    }
}
