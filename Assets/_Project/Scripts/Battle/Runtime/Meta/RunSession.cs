/****************************************************
    文件：RunSession.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 17:00:00
	功能：本次运行的选关、技能、背包与通关状态，跨场景保留。
*****************************************************/

using System.Collections.Generic;

namespace PrismTactics.Battle
{
    public static class RunSession
    {
        private static readonly Dictionary<string, int> SelectedSkills =
            new Dictionary<string, int>();

        private static readonly List<string> Relics = new List<string>();
        private static readonly HashSet<string> ClearedLevels = new HashSet<string>();

        public static int SelectedLevelIndex { get; set; } = -1;
        public static string SelectedLevelId { get; set; } = string.Empty;
        public static string PendingRelicId { get; set; } = string.Empty;

        public static IReadOnlyList<string> RelicIds => Relics;
        public static int RelicCount => Relics.Count;

        public static int GetSelectedSkillIndex(string operatorId)
        {
            return SelectedSkills.TryGetValue(operatorId, out int index) ? index : 0;
        }

        public static void SetSelectedSkillIndex(string operatorId, int index)
        {
            if (string.IsNullOrEmpty(operatorId))
            {
                return;
            }

            SelectedSkills[operatorId] = index;
        }

        public static bool HasRelic(string relicId)
        {
            return !string.IsNullOrEmpty(relicId) && Relics.Contains(relicId);
        }

        public static bool AddRelic(string relicId)
        {
            if (string.IsNullOrEmpty(relicId) || Relics.Contains(relicId))
            {
                return false;
            }

            Relics.Add(relicId);
            return true;
        }

        public static bool MarkCleared(string levelId)
        {
            return !string.IsNullOrEmpty(levelId) && ClearedLevels.Add(levelId);
        }

        public static bool IsCleared(string levelId)
        {
            return !string.IsNullOrEmpty(levelId) && ClearedLevels.Contains(levelId);
        }

        /// <summary>
        /// 性能基准专用：清空本局选择、藏品和通关状态，保证多次采样的战斗数值一致。
        /// 正常流程不要调用，否则会丢掉玩家当前局的选择。
        /// </summary>
        public static void ResetForBenchmark()
        {
            SelectedSkills.Clear();
            Relics.Clear();
            ClearedLevels.Clear();
            SelectedLevelIndex = -1;
            SelectedLevelId = string.Empty;
            PendingRelicId = string.Empty;
        }
    }
}
