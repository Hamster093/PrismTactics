/****************************************************
    文件：M1BattleSmokeTests.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 16:35:00
	功能：M1 场景冒烟测试：不部署干员，验证 3 波敌人全部漏怪并失败。
*****************************************************/

using System.Collections;
using NUnit.Framework;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace PrismTactics.Tests.PlayMode
{
    public sealed class M1BattleSmokeTests
    {
        [UnityTest]
        public IEnumerator MetaFlow_SelectLevelSkillAndBackpack()
        {
            yield return LoadContent();
            SceneManager.LoadScene("M1_Battle");
            yield return null;
            yield return null;
            yield return null;

            var levelPanel = UIManager.Instance.Get<LevelSelectPanel>();
            Assert.That(levelPanel, Is.Not.Null);
            Assert.That(levelPanel.IsOpen, Is.True);
            Assert.That(GameObject.Find("LevelInfoPanel"), Is.Null);

            levelPanel.gameObject.transform.Find("LevelNodes/LevelNode1")
                .GetComponent<Button>().onClick.Invoke();
            yield return null;

            Assert.That(GameObject.Find("LevelInfoPanel"), Is.Not.Null);
            GameObject.Find("StartActionButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;

            Assert.That(UIManager.Instance.IsOpen<SkillSelectPanel>(), Is.True);
            Transform card1 = GameObject.Find("OperatorCard1").transform;
            Assert.That(card1.Find("FilledContent").gameObject.activeSelf, Is.True);
            Transform card5 = GameObject.Find("OperatorCard5").transform;
            Assert.That(card5.Find("FilledContent").gameObject.activeSelf, Is.True);
            Transform card6 = GameObject.Find("OperatorCard6").transform;
            Assert.That(card6.Find("FilledContent").gameObject.activeSelf, Is.False);
            Assert.That(card6.GetComponent<Image>().sprite, Is.Not.Null);

            Transform skill2 = card1.Find("FilledContent/SkillButton2");
            Assert.That(skill2, Is.Not.Null);
            skill2.GetComponent<Button>().onClick.Invoke();
            Assert.That(RunSession.GetSelectedSkillIndex("1"), Is.EqualTo(1));

            GameObject backpackButton = GameObject.Find("BackpackButton");
            Assert.That(backpackButton, Is.Not.Null);
            backpackButton.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(UIManager.Instance.IsOpen<BackpackPanel>(), Is.True);
            backpackButton.GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(UIManager.Instance.IsOpen<BackpackPanel>(), Is.False);

            GameObject.Find("BackButton").GetComponent<Button>().onClick.Invoke();
            yield return null;
            Assert.That(UIManager.Instance.IsOpen<LevelSelectPanel>(), Is.True);
        }

        [UnityTest]
        public IEnumerator M1Scene_LeakAndDefeat()
        {
            yield return LoadContent();
            SceneManager.LoadScene("M1_Battle");
            yield return null;
            yield return null;

            Assert.That(Object.FindObjectOfType<MetaFlowController>(), Is.Not.Null);
            var levelNode = GameObject.Find("LevelNode1");
            Assert.That(levelNode, Is.Not.Null);
            levelNode.GetComponent<Button>().onClick.Invoke();
            yield return null;

            var startAction = GameObject.Find("StartActionButton");
            Assert.That(startAction, Is.Not.Null);
            startAction.GetComponent<Button>().onClick.Invoke();
            yield return null;

            var startBattle = GameObject.Find("StartBattleButton");
            Assert.That(startBattle, Is.Not.Null);
            startBattle.GetComponent<Button>().onClick.Invoke();
            yield return null;
            yield return null;

            BattleController controller = Object.FindObjectOfType<BattleController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.Operators.Count, Is.EqualTo(5));
            Assert.That(controller.Operators[0].skills.Length, Is.EqualTo(2));
            Assert.That(Object.FindObjectOfType<BattleHudBinder>(), Is.Not.Null);
            Assert.That(UIManager.Instance, Is.Not.Null);
            Assert.That(UIManager.Instance.Get<OperatorDetailPanel>(), Is.Not.Null);
            Assert.That(UIManager.Instance.Get<OperatorActionPanel>(), Is.Not.Null);
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.False);

            UIManager.Instance.Open<OperatorDetailPanel>();
            UIManager.Instance.Open<OperatorActionPanel>();
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.True);
            Assert.That(UIManager.Instance.IsOpen<OperatorActionPanel>(), Is.True);
            UIManager.Instance.CloseAll();
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.False);

            var drag = Object.FindObjectOfType<BattleDeploymentDragController>();
            Assert.That(drag, Is.Not.Null);
            Assert.That(Object.FindObjectsOfType<DeployCardDragHandler>().Length,
                Is.EqualTo(controller.Operators.Count), "每个干员应生成一张可拖拽部署卡。");
            drag.BeginCardDrag(controller.Operators[0]);
            Assert.That(Time.timeScale, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.True);
            drag.EndCardDrag(new Vector2(-1000f, -1000f));
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.False);

            BattleHudBinder hud = Object.FindObjectOfType<BattleHudBinder>();
            drag.ShowReserveDetail(controller.Operators[0]);
            Assert.That(Time.timeScale, Is.EqualTo(0.1f).Within(0.001f));
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.True);
            hud.ClearPreview();
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(UIManager.Instance.IsOpen<OperatorDetailPanel>(), Is.False);

            Transform queue = GameObject.Find("HUD").transform.Find("DeployQueuePanel");
            Transform firstSlot = queue.Find("DeploySlot1");
            Assert.That(firstSlot.gameObject.activeSelf, Is.True);

            Vector2Int deployCell = new Vector2Int(-1, -1);
            for (int x = 0; x < controller.Map.Width && deployCell.x < 0; x++)
            {
                for (int y = 0; y < controller.Map.Height; y++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (controller.CanDeployAt(controller.Operators[0], candidate))
                    {
                        deployCell = candidate;
                        break;
                    }
                }
            }

            Assert.That(deployCell.x, Is.GreaterThanOrEqualTo(0));
            Assert.That(controller.TryDeploy(controller.Operators[0], deployCell, GridFacing.Right), Is.True);
            Assert.That(firstSlot.gameObject.activeSelf, Is.False);
            CombatEntity deployed = controller.Allies[controller.Allies.Count - 1];
            Assert.That(deployed.GetComponentInChildren<SpineUnitView>(true), Is.Not.Null);
            Assert.That(deployed.transform.Find("StatusBars"), Is.Not.Null);
            Assert.That(deployed.GetComponent<CombatFeedback>(), Is.Not.Null);
            Assert.That(controller.TryRetreat(deployed), Is.True);
            Assert.That(firstSlot.gameObject.activeSelf, Is.True);

            Time.timeScale = 8f;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (controller.State == BattleState.Running && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Time.timeScale = 1f;

            Assert.That(controller.State, Is.EqualTo(BattleState.Defeat));
            Assert.That(controller.Life, Is.EqualTo(0));
            Assert.That(controller.TotalEnemies, Is.GreaterThan(0));
        }

        private static IEnumerator LoadContent()
        {
            yield return ContentService.LoadAsync();
            Assert.That(ContentService.IsLoaded, Is.True, ContentService.LastError);
        }
    }
}
