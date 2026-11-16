/****************************************************
    文件：DropRelicCardClick.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/11 08:40:00
	功能：掉落卡点击转发：卡片上任意 Graphic 被点到都算拾取这张卡。
*****************************************************/

using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 挂在掉落卡及其子节点上：点中任意带射线的 UI 元素都触发同一张卡的拾取。
    /// 不依赖 Button.targetGraphic，避免卡片根节点没有 Graphic 时点不动。
    /// </summary>
    public sealed class DropRelicCardClick : MonoBehaviour, IPointerClickHandler
    {
        private string _relicId;
        private Action<string> _onClick;

        public void Initialize(string relicId, Action<string> onClick)
        {
            _relicId = relicId;
            _onClick = onClick;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("[Drop] 点击命中：" + name + " → " + _relicId);
            _onClick?.Invoke(_relicId);
        }
    }
}
