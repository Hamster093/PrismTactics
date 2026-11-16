/****************************************************
    文件：PanelBase.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 12:00:00
	功能：面板基类：默认非模态，提供打开、关闭、刷新生命周期。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public abstract class PanelBase : MonoBehaviour, IPanel
    {
        public bool IsOpen { get; protected set; }

        // 默认非模态，需要遮罩的面板自行 override
        public virtual bool IsModal => false;

        protected virtual void Awake()
        {
            OnInit();
        }

        public virtual void OnInit()
        {
        }

        public virtual void OnOpen(object data = null)
        {
            gameObject.SetActive(true);
            IsOpen = true;
        }

        public virtual void OnClose()
        {
            IsOpen = false;
            gameObject.SetActive(false);
        }

        public virtual void OnRefresh(object data = null)
        {
        }

        public virtual void OnDestroy()
        {
        }
    }
}
