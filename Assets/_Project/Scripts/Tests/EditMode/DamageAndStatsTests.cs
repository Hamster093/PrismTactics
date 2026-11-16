/****************************************************
    文件：DamageAndStatsTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：伤害公式、伤害管线与属性块的 EditMode 测试。
*****************************************************/

using NUnit.Framework;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Combat.Tests
{
    public sealed class DamageAndStatsTests
    {
        private sealed class FakeTarget : IDamageable
        {
            public Faction Faction => PrismTactics.Combat.Faction.Enemy;
            public bool IsAlive => CurrentHealth > 0f;
            public float CurrentHealth { get; private set; }
            public float MaxHealth { get; }
            public StatBlock Stats { get; } = new StatBlock();

            public FakeTarget(float health, float defense, float resistance)
            {
                MaxHealth = health;
                CurrentHealth = health;
                Stats.SetBase(StatType.MaxHealth, health);
                Stats.SetBase(StatType.Defense, defense);
                Stats.SetBase(StatType.MagicResist, resistance);
            }

            public void TakeDamage(in DamageResult result)
            {
                CurrentHealth = Mathf.Max(0f, CurrentHealth - result.FinalAmount);
            }
        }

        [Test]
        public void PhysicalDamage_SubtractsDefense()
        {
            Assert.That(
                DamageMath.Mitigate(100f, DamageType.Physical, 20f, 0f),
                Is.EqualTo(80f).Within(0.001f));
        }

        [Test]
        public void PhysicalDamage_HasFivePercentFloor()
        {
            Assert.That(
                DamageMath.Mitigate(100f, DamageType.Physical, 500f, 0f),
                Is.EqualTo(5f).Within(0.001f));
        }

        [Test]
        public void ArtsDamage_UsesResistance()
        {
            Assert.That(
                DamageMath.Mitigate(100f, DamageType.Arts, 0f, 30f),
                Is.EqualTo(70f).Within(0.001f));
        }

        [Test]
        public void TrueDamage_IgnoresDefenseAndResistance()
        {
            Assert.That(
                DamageMath.Mitigate(100f, DamageType.True, 999f, 99f),
                Is.EqualTo(100f).Within(0.001f));
        }

        [Test]
        public void DamagePipeline_AppliesMitigationAndWritesHealth()
        {
            var target = new FakeTarget(100f, 20f, 0f);
            var pipeline = new DamagePipeline();
            var info = new DamageInfo(null, target, DamageType.Physical, 100f);

            DamageResult result = pipeline.Apply(in info);

            Assert.That(result.FinalAmount, Is.EqualTo(80f).Within(0.001f));
            Assert.That(target.CurrentHealth, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void StatBlock_AppliesFlatThenPercentAndCanRemoveBySource()
        {
            var stats = new StatBlock();
            stats.SetBase(StatType.Attack, 100f);
            var source = new object();

            stats.AddModifier(new StatModifier(StatType.Attack, 20f, 0.5f, source));

            Assert.That(stats.Get(StatType.Attack), Is.EqualTo(180f).Within(0.001f));

            stats.RemoveModifiersFrom(source);

            Assert.That(stats.Get(StatType.Attack), Is.EqualTo(100f).Within(0.001f));
        }
    }
}
