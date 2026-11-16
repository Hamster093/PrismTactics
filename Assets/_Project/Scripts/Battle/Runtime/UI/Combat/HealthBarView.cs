/****************************************************
    文件：HealthBarView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：单位血条，只订阅 HealthComponent 变化事件。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class HealthBarView : MonoBehaviour
    {
        [SerializeField] private FillBarRenderer fill;

        private CombatEntity _entity;
        private bool _showOnlyWhenDamaged;

        /// <summary>
        /// 绑定单位。showOnlyWhenDamaged 用于敌人：满血时隐藏血条，第一次受伤才显示。
        /// </summary>
        public void Bind(CombatEntity entity, bool showOnlyWhenDamaged = false)
        {
            Unbind();
            _entity = entity;
            _showOnlyWhenDamaged = showOnlyWhenDamaged;
            if (_entity == null || _entity.Health == null)
            {
                return;
            }

            _entity.Health.HealthChanged += HandleHealthChanged;
            HandleHealthChanged(_entity.CurrentHealth, _entity.MaxHealth);
        }

        public void Unbind()
        {
            if (_entity != null && _entity.Health != null)
            {
                _entity.Health.HealthChanged -= HandleHealthChanged;
            }

            _entity = null;
        }

        private void HandleHealthChanged(float current, float max)
        {
            if (fill != null)
            {
                fill.SetFill(max > 0f ? current / max : 0f);
            }

            // 敌人满血时整条隐藏；干员保持常显。
            bool visible = !_showOnlyWhenDamaged || current < max - 0.0001f;
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
