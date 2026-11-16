/****************************************************
    文件：HotUpdateDeployTool.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/11 08:10:00
	功能：把构建好的热更/内容包复制进已生成的 Player，模拟一次热更替换，无需重新出包。
*****************************************************/

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 一键替换到 Player：把 Assets/StreamingAssets 下的 yoo 与 Release 覆盖到目标 Player 的 _Data/StreamingAssets。
    /// 用于演示「出热更包 → 替换资源 → 重启 Player 生效」的完整链路。
    /// </summary>
    public static class HotUpdateDeployTool
    {
        private const string StreamingRoot = "Assets/StreamingAssets/yoo";
        private const string ReleaseRoot = "Assets/StreamingAssets/Release";
        private const string BuildsRoot = "Builds";
        private const string PreferredPlayerDir = "Builds/M3_0_IL2CPP";

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.3 出包/2.3.2 一键替换到 Player（模拟热更）", false, 41)]
        public static void Deploy()
        {
            if (!Directory.Exists(StreamingRoot))
            {
                EditorUtility.DisplayDialog("热更替换", "找不到构建产物：" + StreamingRoot + "\n请先构建 HotUpdateRes 与 BattleRes。", "好");
                return;
            }

            string playerDir = ResolvePlayerDirectory();
            if (playerDir == null)
            {
                EditorUtility.DisplayDialog("热更替换", "在 Builds 下找不到 Player（含 *_Data 目录）。\n请先出一次包，或手动指定目录。", "好");
                return;
            }

            string dataDir = FindDataDirectory(playerDir);
            if (dataDir == null)
            {
                EditorUtility.DisplayDialog("热更替换", "Player 目录里没有 *_Data：" + playerDir, "好");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "确认替换",
                    "目标 Player：\n" + playerDir + "\n\n会用当前 StreamingAssets 覆盖该 Player 的资源和发布清单，确定？",
                    "替换", "取消"))
            {
                return;
            }

            try
            {
                string destStreaming = Path.Combine(dataDir, "StreamingAssets");
                CopyDirectory(StreamingRoot, Path.Combine(destStreaming, "yoo"), true);
                CopyDirectory(ReleaseRoot, Path.Combine(destStreaming, "Release"), true);
                Debug.Log($"[热更替换] 完成：{StreamingRoot} + {ReleaseRoot} → {destStreaming}");
                EditorUtility.DisplayDialog("热更替换", "替换完成，直接启动 Player 即可验证新内容。", "好");
            }
            catch (Exception exception)
            {
                Debug.LogError("[热更替换] 失败：" + exception);
                EditorUtility.DisplayDialog("热更替换失败", exception.Message, "好");
            }
        }

        /// <summary>优先用固定目录，找不到就在 Builds 下挑最新的 Player。</summary>
        private static string ResolvePlayerDirectory()
        {
            if (Directory.Exists(PreferredPlayerDir) && FindDataDirectory(PreferredPlayerDir) != null)
            {
                return PreferredPlayerDir;
            }

            if (!Directory.Exists(BuildsRoot))
            {
                return null;
            }

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string dir in Directory.GetDirectories(BuildsRoot))
            {
                if (FindDataDirectory(dir) == null)
                {
                    continue;
                }

                DateTime time = Directory.GetLastWriteTime(dir);
                if (time > bestTime)
                {
                    bestTime = time;
                    best = dir;
                }
            }

            return best;
        }

        private static string FindDataDirectory(string playerDir)
        {
            if (!Directory.Exists(playerDir))
            {
                return null;
            }

            string[] dataDirs = Directory.GetDirectories(playerDir, "*_Data");
            return dataDirs.Length > 0 ? dataDirs[0] : null;
        }

        /// <summary>递归复制目录；clear 为 true 时先清空目标，避免残留旧 bundle。</summary>
        private static void CopyDirectory(string source, string destination, bool clear)
        {
            if (clear && Directory.Exists(destination))
            {
                Directory.Delete(destination, true);
            }

            Directory.CreateDirectory(destination);
            foreach (string file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }

            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), false);
            }
        }
    }
}
