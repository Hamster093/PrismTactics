/****************************************************
    文件：MapVisualConfig.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 13:00:00
	功能：地图表现配置：地面/高台/基地/出生点格子 Prefab 与背景 Prefab。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    [CreateAssetMenu(
        fileName = "MapVisualConfig",
        menuName = "PrismTactics/地图表现配置")]
    public sealed class MapVisualConfig : ScriptableObject
    {
        [Header("格子 Prefab")]
        public GameObject groundCellPrefab;
        public GameObject highGroundCellPrefab;
        public GameObject baseCellPrefab;
        public GameObject spawnCellPrefab;

        [Header("背景")]
        public GameObject backgroundPrefab;
    }
}
