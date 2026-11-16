/****************************************************
    文件：ProjectileView.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 17:00:00
    功能：追踪目标的普通攻击弹道，命中时结算伤害；结束时交给对象池回收。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class ProjectileView : MonoBehaviour
    {
        [SerializeField] private float hitDistance = 0.18f;

        private CombatEntity _owner;
        private CombatEntity _target;
        private float _speed;
        private Renderer _renderer;

        public void Launch(CombatEntity owner, CombatEntity target, float speed)
        {
            Launch(owner, target, speed, Color.white);
        }

        public void Launch(CombatEntity owner, CombatEntity target, float speed, Color tint)
        {
            _owner = owner;
            _target = target;
            _speed = Mathf.Max(0.1f, speed);

            if (_renderer == null)
            {
                _renderer = GetComponentInChildren<Renderer>();
            }

            if (_renderer != null)
            {
                _renderer.material.color = tint;
            }

            if (_owner != null)
            {
                transform.position = _owner.transform.position + Vector3.up * 0.7f;
            }
        }

        /// <summary>回收前清空引用，避免池对象继续持有已销毁单位。</summary>
        public void ResetForPool()
        {
            _owner = null;
            _target = null;
            _speed = 0f;
            if (_renderer != null)
            {
                _renderer.material.color = Color.white;
            }
        }

        private void Update()
        {
            if (_owner == null || _target == null || !_target.IsAlive)
            {
                ProjectileManager.Release(this);
                return;
            }

            Vector3 targetPosition = _target.transform.position + Vector3.up * 0.45f;
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                _speed * Time.deltaTime);

            if ((transform.position - targetPosition).sqrMagnitude
                <= hitDistance * hitDistance)
            {
                _owner.PerformProjectileAttack(_target);
                ProjectileManager.Release(this);
            }
        }
    }
}
