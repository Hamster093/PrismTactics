/****************************************************
    文件：CombatFeedback.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：把单位受伤事件桥接到伤害数字，不让战斗逻辑直接依赖 UI。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class CombatFeedback : MonoBehaviour
    {
        private CombatEntity _entity;
        private DamageNumberManager _damageNumbers;

        public void Initialize(CombatEntity entity, DamageNumberManager damageNumbers)
        {
            Unbind();
            _entity = entity;
            _damageNumbers = damageNumbers;
            if (_entity != null && _entity.Health != null)
            {
                _entity.Health.Damaged += HandleDamaged;
            }
        }

        private void HandleDamaged(DamageResult result)
        {
            if (_damageNumbers == null || result.FinalAmount <= 0f)
            {
                return;
            }

            string content = Mathf.CeilToInt(result.FinalAmount).ToString();
            Color color = _entity != null && _entity.Faction == Faction.Enemy
                ? new Color(1f, 0.88f, 0.3f, 1f)
                : new Color(1f, 0.3f, 0.3f, 1f);
            _damageNumbers.Spawn(transform.position, content, color);
        }

        private void Unbind()
        {
            if (_entity != null && _entity.Health != null)
            {
                _entity.Health.Damaged -= HandleDamaged;
            }

            _entity = null;
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
