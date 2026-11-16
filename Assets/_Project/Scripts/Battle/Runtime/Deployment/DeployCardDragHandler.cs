/****************************************************
    文件：DeployCardDragHandler.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 12:30:00
	功能：干员卡拖拽事件转发，把卡牌拖动交给部署控制器处理。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 干员卡拖拽事件转发器：挂在 UI 卡牌上，实现 Unity EventSystem 的拖拽/点击接口，
    /// 把事件原样转给 BattleDeploymentDragController 处理。
    /// 本类只做"转发"，不含任何部署逻辑——具体怎么拖、拖到哪、松开后做什么，
    /// 全部由控制器决定，从而把 UI 事件与部署逻辑解耦。
    /// </summary>
    public sealed class DeployCardDragHandler : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IPointerClickHandler
    {
        #region 字段与初始化
        // 事件接收方（部署拖拽控制器），可为空；为空时所有回调静默跳过
        private BattleDeploymentDragController _owner;
        // 本卡牌对应的干员配置，转发事件时一并带上，控制器据此知道"拖的是谁"
        private OperatorConfig _operator;

        public void Initialize(BattleDeploymentDragController owner, OperatorConfig operatorConfig)
        {
            _owner = owner;
            _operator = operatorConfig;
        }
        #endregion

        #region 拖拽事件转发
        /// <summary>
        /// 开始拖拽：通知控制器"这张卡被拿起来了"。
        /// /// 控制器创建跟随鼠标的预览/高亮可部署格子。
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            _owner?.BeginCardDrag(_operator);
        }
        /// <summary>
        /// 拖拽中：把当前指针屏幕坐标转给控制器。
        /// 控制器据此更新预览位置、检测鼠标下的格子是否可部署。
        /// 注意传的是 eventData.position（屏幕坐标），不是世界坐标。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            _owner?.DragCard(eventData.position);
        }
        /// <summary>
        /// 结束拖拽：把松开时的指针坐标转给控制器。
        /// 控制器据此判断"落在哪个格子、能否部署"，完成部署或取消。
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            _owner?.EndCardDrag(eventData.position);
        }
        #endregion
        #region 点击事件转发
        /// <summary>
        /// 点击卡牌：通知控制器显示该干员的预备详情（属性/技能等）。
        /// 与拖拽是两条独立路径——短按看详情，长按拖动部署。
        /// </summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            _owner?.ShowReserveDetail(_operator);
        }
        #endregion
    }
}
