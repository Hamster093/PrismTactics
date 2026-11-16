/****************************************************
    文件：MetaConfigData.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 17:00:00
	功能：关卡、藏品与本次运行状态的配置数据结构。
*****************************************************/

using System;
using System.Collections.Generic;

namespace PrismTactics.Battle
{
    [Serializable]
    public class LevelDefinition
    {
        public string id = "level_1";
        public string displayName = "第一关";
        public string mapId = "M1_Test";
    }

    [Serializable]
    public class LevelLibrary
    {
        public List<LevelDefinition> levels = new List<LevelDefinition>();
    }

    [Serializable]
    public class RelicDefinition
    {
        public string id = "relic";
        public string displayName = "藏品";
        public string description = string.Empty;
        public string icon = string.Empty;
        public int rarity = 1;
        public float attackPercent;
        public float healthPercent;
        public float defensePercent;
        public float attackSpeedPercent;
    }

    [Serializable]
    public class RelicLibrary
    {
        public List<RelicDefinition> relics = new List<RelicDefinition>();
    }
}
