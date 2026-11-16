/****************************************************
    文件：PerfPlayerBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 13:00:00
    功能：构建只包含 M1_Perf60 的 Development Player，用于真机/Player 性能基线。
*****************************************************/

using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 生成一个独立的性能测试 Player，不参与正常游戏发布。
    /// 场景只包含 M1_Perf60，启动后自动跑 60 敌人基准。
    /// </summary>
    public static class PerfPlayerBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/M1_Perf60.unity";

        [MenuItem("Tools/PrismTactics/9 其他/9.2 性能/9.2.4 构建 Player（60 敌人）")]
        public static void BuildFromMenu()
        {
            Build();
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.PerfPlayerBuilder.BuildFromCli</summary>
        public static void BuildFromCli()
        {
            Build();
        }

        public static void Build()
        {
            string projectRoot = Directory.GetCurrentDirectory();
            string outputDirectory = Path.Combine(projectRoot, "Builds", "Perf60");
            Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(outputDirectory, "PrismTacticsPerf.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log($"[PerfPlayer] 构建结果：{summary.result}，" +
                      $"大小 {summary.totalSize / (1024 * 1024)} MB，" +
                      $"错误 {summary.totalErrors}，输出 {options.locationPathName}");

            if (summary.result != BuildResult.Succeeded)
            {
                throw new System.InvalidOperationException(
                    $"性能 Player 构建失败：{summary.result}，错误 {summary.totalErrors}");
            }
        }
    }
}
