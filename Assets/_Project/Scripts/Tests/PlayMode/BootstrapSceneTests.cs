/****************************************************
    文件：BootstrapSceneTests.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 15:25:00
    功能：验证 Boot 场景加载内容后自动进入 M1_Battle。
*****************************************************/

using System.Collections;
using NUnit.Framework;
using PrismTactics.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PrismTactics.Tests.PlayMode
{
    public sealed class BootstrapSceneTests
    {
        [UnityTest]
        public IEnumerator Bootstrap_LoadsBattleScene()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;

            float deadline = Time.realtimeSinceStartup + 60f;
            while (SceneManager.GetActiveScene().name != "M1_Battle"
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(ContentService.IsLoaded, Is.True, ContentService.LastError);
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("M1_Battle"),
                "Boot 场景没有在 60 秒内进入 M1_Battle");

            yield return null;
            yield return null;
            Assert.That(Object.FindObjectOfType<MetaFlowController>(), Is.Not.Null);
        }
    }
}
