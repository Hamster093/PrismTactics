/****************************************************
    文件：Phase3PlayerBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 15:45:00
    功能：M3.0 验证：用 IL2CPP 构建 Windows Standalone 最小 Player（仅 M1_Battle），确认工具链可用。
*****************************************************/

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 只负责“能不能出 IL2CPP 包”，不做版本/发布；M3.4 再扩展成完整发布流程。
    /// 输出到独立目录，避免覆盖已有的性能包 Builds/Perf60。
    /// </summary>
    public static class Phase3PlayerBuilder
    {
        private const string OutputDirectory = "Builds/M3_0_IL2CPP";
        private const string OutputName = "PrismTactics.exe";
        private const string BootstrapScene = "Assets/_Project/Scenes/Bootstrap.unity";
        private const string BattleScene = "Assets/_Project/Scenes/M1_Battle.unity";

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.3 出包/2.3.1 构建 Player（IL2CPP）", false, 22)]
        public static void BuildFromMenu()
        {
            BuildWindowsPlayer();
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3PlayerBuilder.BuildFromCli</summary>
        public static void BuildFromCli()
        {
            int exitCode = 0;
            try
            {
                BuildWindowsPlayer();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[M3.0] IL2CPP 构建失败：{exception}");
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>
        /// 强制 IL2CPP + StandaloneWindows64，打 Bootstrap + M1_Battle 两个场景。
        /// </summary>
        public static void BuildWindowsPlayer()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new Exception("当前编辑器没有安装 Windows Build Support（StandaloneWindows64）");

            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);

            // 录屏用窗口模式，避免全屏/最大化导致 OBS 抓不到或切窗困难。
            PlayerSettings.fullScreenMode = UnityEngine.FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;

            if (!File.Exists(BootstrapScene))
                throw new Exception($"Bootstrap 场景不存在：{BootstrapScene}");
            if (!File.Exists(BattleScene))
                throw new Exception($"战斗场景不存在：{BattleScene}");

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.Combine(projectRoot, OutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            // Player 走 OfflinePlayMode，发布清单和内置内容必须一起刷新。
            Phase3ReleaseBuilder.SyncReleaseToStreaming();
            string contentVersion = Phase3ContentBuilder.GetTargetVersion();
            YooAsset.Editor.BuildResult contentResult = Phase3ContentBuilder.BuildContent(contentVersion, true);
            if (!contentResult.Success)
                throw new Exception("BattleRes 内容包构建失败：" + contentResult.ErrorInfo);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { BootstrapScene, BattleScene },
                locationPathName = Path.Combine(outputDirectory, OutputName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };

            Debug.Log($"[M3.0] 开始 IL2CPP 构建：{options.locationPathName}");
            DateTime start = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(options);
            TimeSpan elapsed = DateTime.Now - start;

            BuildSummary summary = report.summary;
            Debug.Log($"[M3.0] 构建结果={summary.result}，耗时={elapsed.TotalMinutes:F1} 分钟，" +
                      $"产物={summary.totalSize / 1024f / 1024f:F1} MB，错误={summary.totalErrors}，警告={summary.totalWarnings}");

            foreach (string step in report.steps
                         .SelectMany(s => s.messages)
                         .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                         .Select(m => m.content)
                         .Take(20))
            {
                Debug.LogError($"[M3.0] 构建错误：{step}");
            }

            if (summary.result != BuildResult.Succeeded)
                throw new Exception($"IL2CPP 构建失败：{summary.result}");

            Debug.Log($"[M3.0] IL2CPP 空包构建成功：{options.locationPathName}");
        }
    }
}
