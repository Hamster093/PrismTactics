/****************************************************
    文件：BattleDebugTools.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/11 09:00:00
	功能：开发期调试工具：一键结束战斗（F10 / 屏幕按钮），用于快速验证掉落与结算流程。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 调试工具：按 F10 或点右上角按钮，立即判定胜利并走正常的结算/掉落流程。
    /// 只在编辑器或 Development Build 里创建，正式包不会出现。
    /// </summary>
    public sealed class BattleDebugTools : MonoBehaviour
    {
        private BattleController _controller;

        public static BattleDebugTools Create(BattleController controller)
        {
            var go = new GameObject("Battle Debug Tools");
            BattleDebugTools tools = go.AddComponent<BattleDebugTools>();
            tools._controller = controller;
            return tools;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
            {
                EndBattle();
            }
        }

        private void OnGUI()
        {
            if (GUI.Button(new Rect(Screen.width - 170f, 12f, 158f, 42f), "结束战斗 (F10)"))
            {
                EndBattle();
            }
        }

        private void EndBattle()
        {
            if (_controller != null)
            {
                _controller.ForceVictory();
            }
        }
    }
}
