/****************************************************
    文件：IPanel.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 12:00:00
	功能：UI 面板生命周期接口，由 UIManager 统一管理。
*****************************************************/

namespace PrismTactics.Battle
{
    public interface IPanel
    {
        bool IsOpen { get; }
        bool IsModal { get; }
        void OnInit();
        void OnOpen(object data = null);
        void OnClose();
        void OnRefresh(object data = null);
        void OnDestroy();
    }
}
