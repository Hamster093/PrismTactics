/****************************************************
    文件：AttackCycleTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：攻击循环的 EditMode 单元测试。
*****************************************************/

using NUnit.Framework;
using PrismTactics.Combat;

namespace PrismTactics.Combat.Tests
{
    public sealed class AttackCycleTests
    {
        private sealed class FakeOwner : IAttackCycleOwner
        {
            public bool HasTarget = true;
            public int Attacks;

            public bool HasValidTarget()
            {
                return HasTarget;
            }

            public void PerformAttack()
            {
                Attacks++;
            }
        }

        [Test]
        public void AttacksAfterWindup_ThenWaitsFullInterval()
        {
            var owner = new FakeOwner();
            var cycle = new AttackCycle(10, 3);

            for (int i = 0; i < 3; i++)
            {
                cycle.Tick(owner);
            }

            Assert.That(owner.Attacks, Is.EqualTo(0));

            cycle.Tick(owner);

            Assert.That(owner.Attacks, Is.EqualTo(1));
            Assert.That(cycle.State, Is.EqualTo(AttackCycleState.Recovery));

            for (int i = 0; i < 9; i++)
            {
                cycle.Tick(owner);
            }

            Assert.That(owner.Attacks, Is.EqualTo(1));
            Assert.That(cycle.State, Is.EqualTo(AttackCycleState.Windup));

            cycle.Tick(owner);

            Assert.That(owner.Attacks, Is.EqualTo(2));
            Assert.That(cycle.State, Is.EqualTo(AttackCycleState.Recovery));
        }

        [Test]
        public void LosingTargetDuringWindup_CancelsAttack()
        {
            var owner = new FakeOwner();
            var cycle = new AttackCycle(10, 3);

            cycle.Tick(owner);
            owner.HasTarget = false;
            cycle.Tick(owner);

            Assert.That(owner.Attacks, Is.EqualTo(0));
            Assert.That(cycle.State, Is.EqualTo(AttackCycleState.Ready));
        }
    }
}
