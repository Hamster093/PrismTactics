/****************************************************
    文件：SpCostBarView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：干员技力条：充能绿色，技能期间橙色并按时长衰减。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class SpCostBarView : MonoBehaviour
    {
        [SerializeField] private FillBarRenderer fill;
        [SerializeField] private GameObject spRoot;

        private CombatEntity _entity;
        private SkillComponent _skill;
        private static readonly Color ChargingColor = new Color(0.25f, 0.9f, 0.42f, 1f);
        private static readonly Color ActiveColor = new Color(1f, 0.55f, 0.12f, 1f);

        public void Bind(CombatEntity entity)
        {
            Unbind();
            _entity = entity;
            _skill = entity != null ? entity.Skills : null;

            if (_skill == null || !_skill.ShowSpBar)
            {
                if (spRoot != null)
                {
                    spRoot.SetActive(false);
                }

                return;
            }

            if (spRoot != null)
            {
                spRoot.SetActive(true);
            }

            _skill.SpChanged += Refresh;
            Refresh();
        }

        public void Unbind()
        {
            if (_skill != null)
            {
                _skill.SpChanged -= Refresh;
            }

            _skill = null;
            _entity = null;
        }

        private void Refresh()
        {
            if (fill == null || _skill == null)
            {
                return;
            }

            fill.SetFill(_skill.SpFillRatio);
            fill.SetColor(_skill.State == SkillState.Active ? ActiveColor : ChargingColor);
        }

        private void OnDestroy()
        {
            Unbind();
        }
    }
}
