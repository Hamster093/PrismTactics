/****************************************************
    文件：HotUpdateSkillContext.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/3 0:23:38
	功能：热更技能执行上下文：向热更代码暴露查询、伤害、定时器和运行时修改能力。
*****************************************************/

using PrismTactics.Combat;
using System;
using System.Collections.Generic;
using UnityEngine;
namespace PrismTactics.Combat
{
    public sealed class HotUpdateSkillContext : ISkillEffectContext
    {
        private readonly CombatEntity _owner;
        private readonly SkillConfig _skill;

        public CombatEntity Owner => _owner;
        public SkillConfig Skill => _skill;
        public int CurrentTick => HotUpdateTickService.CurrentTick;

        public HotUpdateSkillContext(CombatEntity owner, SkillConfig skill)
        {
            _owner = owner;
            _skill = skill;
        }

        public IReadOnlyList<CombatEntity> EnemiesInRange()
        {
            var results = new List<CombatEntity>(32);
            if (_owner != null && _owner.World != null && _owner.World.Space != null)
            {
                _owner.World.Space.GetTargetsInRange(
                    _owner, _owner.GetEffectiveRange(_skill.rangeCells), results);
            }
            return results;
        }

        public IReadOnlyList<CombatEntity> EnemiesInRadius(Vector3 center, float radius)
        {
            var results = new List<CombatEntity>(32);
            if (_owner != null && _owner.World != null)
            {
                _owner.World.Registry.GetEnemiesInRadius(_owner.Faction, center, radius, results);
            }
            return results;
        }

        public void DealDamage(CombatEntity target, float rawDamage, DamageType type)
        {
            if (_owner == null || _owner.World == null || target == null || !target.IsAlive) return;
            var info = new DamageInfo(_owner, target, type, rawDamage);
            _owner.World.DamagePipeline.Apply(in info);
        }

        public void Heal(CombatEntity target, float amount)
        {
            if (target != null && target.IsAlive) target.Health.Heal(amount);
        }

        public void Schedule(int delayTicks, Action action)
            => HotUpdateTickService.Schedule(delayTicks, action);

        public void ScheduleRepeating(int intervalTicks, int count, Action action)
            => HotUpdateTickService.ScheduleRepeating(intervalTicks, count, action);

        public void SetRangeOverride(Vector2Int[] cells, int durationTicks)
            => _owner.SetRangeOverride(cells, durationTicks);

        public void AddAttackSpeedPercent(float percent, int durationTicks)
            => _owner.AddTimedAttackSpeed(percent, durationTicks);

        public void AddStatModifier(StatType stat, float flat, float percent, int durationTicks)
            => _owner.AddTimedStatModifier(stat, flat, percent, durationTicks);

        public GameObject SpawnVisual(string address, Vector3 position, Quaternion rotation)
            => HotUpdateVisualService.Spawn(address, position, rotation);

        public void DespawnVisual(GameObject visual)
            => HotUpdateVisualService.Instance?.Despawn(visual);
    }
}