/****************************************************
    文件：ItemCardDragHandler.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/09 17:30:00
	功能：道具卡拖拽事件转发，把卡牌拖动交给部署拖拽控制器处理。
*****************************************************/

using UnityEngine;
using UnityEngine.EventSystems;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 道具卡事件转发器：只把拖拽事件转给 BattleDeploymentDragController，不含放置逻辑。
    /// </summary>
    public sealed class ItemCardDragHandler : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        private BattleDeploymentDragController _owner;
        private ItemDefinition _item;

        public void Initialize(BattleDeploymentDragController owner, ItemDefinition item)
        {
            _owner = owner;
            _item = item;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _owner?.BeginItemDrag(_item);
        }

        public void OnDrag(PointerEventData eventData)
        {
            _owner?.DragCard(eventData.position);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _owner?.EndCardDrag(eventData.position);
        }
    }
}
