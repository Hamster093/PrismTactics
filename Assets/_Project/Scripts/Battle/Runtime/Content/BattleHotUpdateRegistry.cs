/****************************************************
    文件：BattleHotUpdateRegistry.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/3 16:32:37
	功能：地图视觉 Provider（AOT 主包和热更 DLL 之间的通信桥梁）
*****************************************************/

using System;

namespace PrismTactics.Battle
{
    // 热更 DLL 里的类只要实现了接口，用于修改地图样式
    public interface IMapVisualProvider
    {
        // 尝试获取某张地图的视觉配置地址
        bool TryGetConfigAddress(string mapId, out string address);
    }

    // 全局注册表（用来存放热更传过来的“实现”）
    public static class BattleHotUpdateRegistry
    {
        //存放当前生效的地图视觉 Provider
        public static IMapVisualProvider MapVisualProvider { get; private set; }
        // 注册方法：热更 DLL 启动时，调用这个方法，把自己的实现类注册进来
        public static void RegisterMapVisualProvider(IMapVisualProvider provider)
        {
            MapVisualProvider = provider;
        }
        // 4重置方法：退出战斗、切场景或者热更卸载时，把MapVisualProvider置空，防止内存泄漏或空引用
        public static void Reset()
        {
            MapVisualProvider = null;
        }
    }
}
