using PrismTactics.Battle;
using PrismTactics.Combat;
using PrismTactics.Launcher;
using UnityEngine;

namespace PrismTactics.HotUpdate
{
    //实现了主包定好的 IHotUpdateEntry 接口，主包通过这个接口来“识别”热更
    public sealed class HotUpdateEntry : IHotUpdateEntry
    {
        // --- 1. 基础信息（供主包在 Boot 界面显示） ---
        public string Version => "1.2.0";
        public string BootMessage => "正式版：黑骑士、黄金史莱姆与黑骑士技能 新地图一张1111111";
        public int StartingCostBonus => 0;

        // 供主包在日志里打印的自检信息
        public string SelfCheck()
        {
            return $"Unity {Application.unityVersion}，热更程序集已加载";
        }

        // --- 2. 核心注册逻辑 ---
        // 主包实例化这个类后，会立刻调用 OnRegister()。
        public void OnRegister()
        {
            // 把热更里写的地图配置判断逻辑），塞进主包的注册表里
            BattleHotUpdateRegistry.RegisterMapVisualProvider(new Map2VisualProvider());
            Debug.Log("[HotUpdate] 注册 MapVisualProvider");

            CombatHotUpdateRegistry.Register(new FrostNovaHandler());
            Debug.Log("[HotUpdate] 注册 frost_nova");
        }

        // --- 3. 具体的业务实现 ---
        // 这是热更 DLL 内部自己写的类。
        // 它实现了主包定义的 IMapVisualProvider 接口，负责告诉主包新地图长什么样。
        private sealed class Map2VisualProvider : IMapVisualProvider
        {
            // 主包加载地图时，会拿着 mapId 来问热更：“这张图归你管吗？”
            public bool TryGetConfigAddress(string mapId, out string address)
            {
                // 如果问的是 "2"，热更回答：“归我管，你去加载这个新地址！”
                if (mapId == "2")
                {
                    address = "MapVisual/MapVisualConfig_Map2";
                    return true;
                }
                // 如果问的是其他的（比如旧图 "1"），热更回答：“不归我管，你走默认逻辑吧。”
                address = null;
                return false;
            }
        }
    }

}