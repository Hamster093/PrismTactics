/****************************************************
    文件：BattleHud.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：M1 战斗 HUD：生命、费用、波次、敌人数量与操作提示。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class BattleHud : MonoBehaviour
    {
        private BattleController _controller;
        private BattleDeploymentController _deployment;
        private GUIStyle _labelStyle;
        private GUIStyle _centerStyle;

        public void Initialize(BattleController controller, BattleDeploymentController deployment)
        {
            _controller = controller;
            _deployment = deployment;
        }

        private void OnGUI()
        {
            if (_controller == null)
            {
                return;
            }

            EnsureStyles();

            GUI.Box(new Rect(10f, 10f, 560f, 96f), GUIContent.none);
            GUI.Label(new Rect(22f, 18f, 540f, 24f), BuildStatusLine(), _labelStyle);
            GUI.Label(new Rect(22f, 44f, 540f, 24f), BuildWaveLine(), _labelStyle);
            GUI.Label(new Rect(22f, 70f, 540f, 24f), BuildHintLine(), _labelStyle);

            if (_controller.State == BattleState.Victory)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f), "胜利", _centerStyle);
            }
            else if (_controller.State == BattleState.Defeat)
            {
                GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 80f), "失败", _centerStyle);
            }
        }

        private string BuildStatusLine()
        {
            string state = _controller.State == BattleState.Running ? "进行中" : "已结束";
            return $"生命 {_controller.Life}/{_controller.MaxLife}    费用 {_controller.Cost}    " +
                   $"剩余敌人 {_controller.RemainingEnemies}/{_controller.TotalEnemies}    " +
                   $"场上 {_controller.LiveEnemies}    状态 {state}";
        }

        private string BuildWaveLine()
        {
            if (_controller.Waves == null)
            {
                return "波次：无配置";
            }

            return $"波次 {_controller.CurrentWave}/{_controller.TotalWaves}    " +
                   $"已生成 {_controller.Waves.SpawnedWaves}/{_controller.TotalWaves}";
        }

        private string BuildHintLine()
        {
            if (_deployment == null || _deployment.Selected == null)
            {
                return "按 1 选择禁卫，点击格子部署；R 旋转朝向；Esc 取消";
            }

            return $"已选择：{_deployment.Selected.displayName}（费用 {_deployment.Selected.deployCost}）    " +
                   $"朝向：{GridFacingUtility.ToChinese(_deployment.Facing)}    R 旋转 / Esc 取消";
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 15;
            _labelStyle.normal.textColor = Color.white;

            _centerStyle = new GUIStyle(GUI.skin.label);
            _centerStyle.fontSize = 42;
            _centerStyle.alignment = TextAnchor.MiddleCenter;
            _centerStyle.normal.textColor = Color.white;
        }

        private void OnDestroy()
        {
            _controller = null;
            _deployment = null;
        }
    }
}
