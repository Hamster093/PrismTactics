using UnityEngine;

namespace PrismTactics.Launcher
{
    public static class HotUpdateRuntime
    {
        public static IHotUpdateEntry Entry { get; private set; }
        public static bool IsLoaded => Entry != null;//成功加载
        public static string Version => Entry != null ? Entry.Version : "AOT";//读版本号，
        public static string BootMessage => Entry != null ? Entry.BootMessage : "AOT 基线";//读开机提示信息
        public static int StartingCostBonus => Entry != null ? Entry.StartingCostBonus : 0;//读初始费用

        // 热更 DLL 加载成功后，调用这个方法 缓存入口对象，之后就可以通过 HotUpdateRuntime 访问热更 DLL 的内容了。
        public static void Register(IHotUpdateEntry entry)
        {
            Entry = entry;
            Debug.Log($"[HotUpdate] 已注册入口：{entry.Version}，{entry.BootMessage}");
        }

        public static void Reset()
        {
            Entry = null;
        }
    }
}
