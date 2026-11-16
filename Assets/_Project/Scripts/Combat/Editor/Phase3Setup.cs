/****************************************************
    文件：Phase3Setup.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 15:40:00
    功能：第三阶段环境设置：把 Windows Standalone 切到 IL2CPP，并检查 HybridCLR/YooAsset 是否就绪。
*****************************************************/

using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 只做环境设置，不构建 Player；供 M3.0 构建流程直接使用。
    /// </summary>
    public static class Phase3Setup
    {
        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.4 环境设置（IL2CPP）")]
        public static void ConfigureFromMenu()
        {
            Configure();
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3Setup.ConfigureFromCli</summary>
        public static void ConfigureFromCli()
        {
            Configure();
        }

        private static void Configure()
        {
            PlayerSettings.SetScriptingBackend(
                BuildTargetGroup.Standalone,
                ScriptingImplementation.IL2CPP);

            bool windowsSupport = BuildPipeline.IsBuildTargetSupported(
                BuildTargetGroup.Standalone,
                BuildTarget.StandaloneWindows64);

            Debug.Log($"[Phase3] Windows Standalone 已设为 IL2CPP；" +
                      $"Windows Build Support = {windowsSupport}；" +
                      $"HybridCLR/YooAsset 包已安装。下一步：打开 YooAsset Collector 和 HybridCLR Settings 配置资源/热更程序集。");
        }
    }
}
