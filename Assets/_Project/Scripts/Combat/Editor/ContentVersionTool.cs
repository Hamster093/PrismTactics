/****************************************************
    文件：ContentVersionTool.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/11 08:20:00
	功能：录制用内容版本切换：1.0（4 干员 / 5 关）与 1.2（5 干员 / 6 关）之间一键备份与还原。
*****************************************************/

using System;
using System.IO;
using System.Text;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 内容版本切换：把 levels.json / operators.json 裁剪成 1.0 的规模，用于录制“旧包 → 热更”的对比。
    /// 切换前自动备份到工程根目录 RecordingBackup，可一键还原。
    /// </summary>
    public static class ContentVersionTool
    {
        private const string BackupDir = "RecordingBackup";
        private const string LevelsFile = "levels.json";
        private const string OperatorsFile = "operators.json";

        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.5 切换到 1.0 内容（4 干员 / 5 关）", false, 60)]
        public static void SwitchToV10()
        {
            if (!EditorUtility.DisplayDialog(
                    "切换到 1.0 内容",
                    "会把 levels.json 裁到 5 关、operators.json 裁到 4 个干员，并备份原文件到 RecordingBackup。\n继续？",
                    "切换", "取消"))
            {
                return;
            }

            try
            {
                Backup();
                LevelLibrary levels = BattleConfigLoader.Load<LevelLibrary>("levels");
                OperatorLibrary operators = BattleConfigLoader.Load<OperatorLibrary>(BattleConfigIds.Operators);
                if (levels == null || operators == null)
                {
                    throw new Exception("读取 levels/operators 失败");
                }

                levels.levels = levels.levels.GetRange(0, Mathf.Min(5, levels.levels.Count));
                operators.operators = operators.operators.GetRange(0, Mathf.Min(4, operators.operators.Count));
                Write("levels", levels);
                Write(BattleConfigIds.Operators, operators);
                AssetDatabase.Refresh();
                Debug.Log($"[内容版本] 已切到 1.0：{levels.levels.Count} 关 / {operators.operators.Count} 干员。");
            }
            catch (Exception exception)
            {
                Debug.LogError("[内容版本] 切换失败：" + exception);
            }
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.6 恢复 1.2 内容（5 干员 / 6 关）", false, 61)]
        public static void RestoreV12()
        {
            string backupLevels = Path.Combine(BackupDir, LevelsFile);
            if (!File.Exists(backupLevels))
            {
                EditorUtility.DisplayDialog("恢复 1.2 内容", "没有找到备份：" + BackupDir, "好");
                return;
            }

            File.Copy(Path.Combine(BackupDir, LevelsFile), Path.GetFullPath(BattleConfigLoader.GetConfigFilePath("levels")), true);
            File.Copy(Path.Combine(BackupDir, OperatorsFile), Path.GetFullPath(BattleConfigLoader.GetConfigFilePath(BattleConfigIds.Operators)), true);
            AssetDatabase.Refresh();
            Debug.Log("[内容版本] 已恢复 1.2 内容。");
        }

        /// <summary>把当前 levels/operators 原文件复制到工程根目录的备份文件夹。</summary>
        private static void Backup()
        {
            Directory.CreateDirectory(BackupDir);
            File.Copy(Path.GetFullPath(BattleConfigLoader.GetConfigFilePath("levels")), Path.Combine(BackupDir, LevelsFile), true);
            File.Copy(Path.GetFullPath(BattleConfigLoader.GetConfigFilePath(BattleConfigIds.Operators)), Path.Combine(BackupDir, OperatorsFile), true);
            Debug.Log("[内容版本] 已备份到 " + Path.GetFullPath(BackupDir));
        }

        private static void Write<T>(string configId, T data) where T : class
        {
            string path = Path.GetFullPath(BattleConfigLoader.GetConfigFilePath(configId));
            File.WriteAllText(path, JsonUtility.ToJson(data, true), new UTF8Encoding(false));
        }
    }
}
