/****************************************************
    文件：UnitPrefabCatalog.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 17:30:00
	功能：干员与敌人的表现 Prefab 目录，避免 Resources 路径绑定中文资源名。
*****************************************************/

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    [CreateAssetMenu(fileName = "UnitPrefabCatalog", menuName = "PrismTactics/战斗/单位 Prefab 目录")]
    public sealed class UnitPrefabCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id;
            public GameObject prefab;
        }

        public List<Entry> operators = new List<Entry>();
        public List<Entry> enemies = new List<Entry>();

        public GameObject GetOperator(string operatorId)
        {
            return Find(operators, operatorId);
        }

        public GameObject GetEnemy(int enemyId)
        {
            return Find(enemies, enemyId.ToString());
        }

        private static GameObject Find(List<Entry> entries, string id)
        {
            if (entries == null || string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry != null && entry.id == id)
                {
                    return entry.prefab;
                }
            }

            return null;
        }
    }
}
