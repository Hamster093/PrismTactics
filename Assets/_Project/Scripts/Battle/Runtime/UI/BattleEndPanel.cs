/****************************************************
    文件：BattleEndPanel.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 01:10:00
	功能：战斗结束覆盖层：胜利/败北图片，点击任意区域后关闭并回调。
*****************************************************/

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PrismTactics.Battle
{
    public sealed class BattleEndPanel : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private GameObject victoryImage;
        [SerializeField] private GameObject defeatImage;

        public event Action Clicked;

        private void Awake()
        {
            if (victoryImage == null)
            {
                Transform victory = transform.Find("胜利");
                victoryImage = victory != null ? victory.gameObject : null;
            }

            if (defeatImage == null)
            {
                Transform defeat = transform.Find("败北");
                defeatImage = defeat != null ? defeat.gameObject : null;
            }
        }

        public void Show(bool victory)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (victoryImage != null)
            {
                victoryImage.SetActive(victory);
            }

            if (defeatImage != null)
            {
                defeatImage.SetActive(!victory);
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke();
        }
    }
}
