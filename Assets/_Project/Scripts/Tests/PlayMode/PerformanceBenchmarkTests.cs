/****************************************************
    文件：PerformanceBenchmarkTests.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 16:15:00
    功能：PlayMode 基准冒烟测试：加载 M1_Perf60，等待 60 敌人压测完成并校验报告。
*****************************************************/

using System.Collections;
using System.IO;
using NUnit.Framework;
using PrismTactics.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace PrismTactics.Tests.PlayMode
{
    public sealed class PerformanceBenchmarkTests
    {
        private const string PerfSceneName = "M1_Perf60";
        private const string PerfScenePath = "Assets/_Project/Scenes/M1_Perf60.unity";
        private const string BossSceneName = "M1_BossStress";
        private const string BossScenePath = "Assets/_Project/Scenes/M1_BossStress.unity";

        [UnityTest]
        public IEnumerator Perf60_RunsAndWritesReport()
        {
            // 压测只加载 AOT 内容，固定部署前 4 个干员；
            // 第 5 个干员（黑骑士）的技能来自热更，会把技能释放的 GC 波动带进对比。
            yield return ContentService.LoadAsync();
            Assert.That(ContentService.IsLoaded, Is.True, ContentService.LastError);

            yield return LoadScene(PerfScenePath, PerfSceneName);

            var runner = Object.FindObjectOfType<PerformanceBenchmarkRunner>();
            Assert.That(runner, Is.Not.Null, "M1_Perf60 场景里没有 PerformanceBenchmarkRunner。");

            float deadline = Time.realtimeSinceStartup + 90f;
            while (!runner.Finished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(runner.Finished, Is.True, "基准场景未在 90 秒内完成。");
            Assert.That(runner.Result, Is.Not.Null, "基准没有生成报告。");
            OperatorLibrary operators = BattleConfigLoader.Load<OperatorLibrary>(BattleConfigIds.Operators);
            Assert.That(operators, Is.Not.Null);
            Assert.That(runner.DeployedEntries.Count, Is.EqualTo(4),
                "压测固定部署前 4 个 AOT 干员，避免热更技能影响对比。");
            Assert.That(runner.Result.totalEnemies, Is.EqualTo(60), "压测波次应包含 60 个敌人。");
            Assert.That(runner.Result.peakLiveEnemies, Is.GreaterThan(10), "同屏峰值异常偏低。");
            Assert.That(runner.Result.overall.frameCount, Is.GreaterThan(30), "采样帧数过少。");
            Assert.That(File.Exists(runner.ReportJsonPath), Is.True, "性能报告 JSON 未写入。");
            Assert.That(File.Exists(runner.ReportMarkdownPath), Is.True, "性能报告 Markdown 未写入。");

            Debug.Log($"[PerfTest] 峰值同屏 {runner.Result.peakLiveEnemies}，" +
                      $"峰值窗口 {runner.Result.peakWindow.avgFps:0.0} FPS，" +
                      $"报告 {runner.ReportJsonPath}");
        }

        /// <summary>
        /// 伤害数字压测：1 个高血量木桩 + 1 个 15 次/秒的速射干员，
        /// 用来观察飘字池在高频伤害下的复用情况与 GC。
        /// </summary>
        [UnityTest]
        public IEnumerator BossStress_RunsAndWritesReport()
        {
            yield return ContentService.LoadAsync();
            Assert.That(ContentService.IsLoaded, Is.True, ContentService.LastError);

            yield return LoadScene(BossScenePath, BossSceneName);

            var runner = Object.FindObjectOfType<PerformanceBenchmarkRunner>();
            Assert.That(runner, Is.Not.Null, "M1_BossStress 场景里没有 PerformanceBenchmarkRunner。");

            float deadline = Time.realtimeSinceStartup + 90f;
            while (!runner.Finished && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(runner.Finished, Is.True, "伤害数字压测未在 90 秒内完成。");
            Assert.That(runner.Result, Is.Not.Null, "伤害数字压测没有生成报告。");
            Assert.That(runner.DeployedEntries.Count, Is.EqualTo(6), "伤害数字压测部署 6 个速射干员（90 次/秒）。");
            Assert.That(File.Exists(runner.ReportJsonPath), Is.True, "压测报告 JSON 未写入。");

            Debug.Log($"[PerfTest] 飘字压测完成，报告 {runner.ReportJsonPath}");
        }

        /// <summary>编辑器内按路径加载场景，避免依赖 Build Settings。</summary>
        private static IEnumerator LoadScene(string path, string sceneName)
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(sceneName);
#endif
            yield return null;
            yield return null;
        }
    }
}
