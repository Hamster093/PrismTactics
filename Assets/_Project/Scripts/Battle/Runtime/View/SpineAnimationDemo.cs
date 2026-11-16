/****************************************************
    文件：SpineAnimationDemo.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 14:20:00
	功能：Spine 演示驱动：按固定间隔轮流传唤每个单位的攻击动画。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 只用于演示场景：所有单位循环播放待机，按间隔轮流播放一次攻击。
    /// 正式战斗里由 <see cref="SpineUnitView"/> 绑定 CombatEntity 自动驱动。
    /// </summary>
    public sealed class SpineAnimationDemo : MonoBehaviour
    {
        [SerializeField] private SpineUnitView[] units;
        [SerializeField] private float attackInterval = 1.5f;
        [SerializeField] private bool autoCycle = true;

        private float _timer;
        private int _index;

        public SpineUnitView[] Units
        {
            get { return units; }
            set { units = value; }
        }

        private void Update()
        {
            if (!autoCycle || units == null || units.Length == 0)
            {
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < attackInterval)
            {
                return;
            }

            _timer = 0f;

            SpineUnitView unit = units[_index];
            if (unit != null && !unit.IsAttacking)
            {
                unit.PlayAttack();
            }

            _index = (_index + 1) % units.Length;
        }
    }
}
