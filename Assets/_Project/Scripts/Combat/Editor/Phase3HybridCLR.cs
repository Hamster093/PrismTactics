/****************************************************
    文件：Phase3HybridCLR.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 16:10:00
    功能：M3.0 验证：检查 HybridCLR 是否已安装并执行 Generate/All，为 IL2CPP 构包准备补丁数据。
*****************************************************/

using System;
using HybridCLR.Commands;
using HybridCLR.Installer;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// HybridCLR 的 IL2CPP 源码需要先通过 Installer 装进 Library/HybridCLR；
    /// 这里不下载，只检查状态并跑生成，避免每次构建前手动点菜单。
    /// </summary>
    public static class Phase3HybridCLR
    {
        [MenuItem("Tools/PrismTactics/2 构建与热更/2.1 热更/2.1.1 生成 HybridCLR", false, 23)]
        public static void GenerateAllFromMenu()
        {
            LogState();
            PrebuildCommand.GenerateAll();
            AssetDatabase.Refresh();
            Debug.Log("[M3.0] HybridCLR Generate/All 完成。");
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3HybridCLR.GenerateAllFromCli</summary>
        public static void GenerateAllFromCli()
        {
            int exitCode = 0;
            try
            {
                LogState();
                PrebuildCommand.GenerateAll();
                AssetDatabase.Refresh();
                Debug.Log("[M3.0] HybridCLR Generate/All 完成。");
            }
            catch (Exception exception)
            {
                Debug.LogError($"[M3.0] HybridCLR Generate/All 失败：{exception}");
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>只读检查，输出安装目录与版本，方便排查“没有初始化 HybridCLR”的构建失败。</summary>
        public static void LogState()
        {
            InstallerController installer = new InstallerController();
            Debug.Log($"[M3.0] HybridCLR 已安装 = {installer.HasInstalledHybridCLR()}；" +
                      $"包版本 = {installer.PackageVersion}；本地 libil2cpp 版本 = {installer.InstalledLibil2cppVersion}");
            Debug.Log($"[M3.0] libil2cpp 目录 = {installer.LocalVersionFile}");
        }
    }
}
