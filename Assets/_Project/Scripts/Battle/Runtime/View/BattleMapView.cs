/****************************************************
    文件：BattleMapView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：地图表现入口：加载 MapVisualConfig，并交给 PrefabMapView 拼装。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    public static class BattleMapView
    {
        private const string ConfigPath = "MapVisualConfig";

        public static void Build(BattleMap map, Transform parent)
        {
            string address = ConfigPath;
            if (BattleHotUpdateRegistry.MapVisualProvider != null
                && BattleHotUpdateRegistry.MapVisualProvider.TryGetConfigAddress(
                    map.MapId, out string custom)
                && !string.IsNullOrEmpty(custom))
            {
                address = custom;
            }
            Debug.Log($"[MapVisual] mapId={map.MapId} config={address}");

            MapVisualConfig config = AssetService.Load<MapVisualConfig>(address);
            if (config == null)
            {
                Debug.LogError(
                    "找不到 GameContent/MapVisualConfig.asset，地图无法生成。");
                return;
            }

            PrefabMapView.Build(map, parent, config);
        }
    }
}
