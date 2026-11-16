/****************************************************
    文件：ResourcesLoadTracker.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 15:10:00
    功能：统计 Resources.Load 调用次数，用于对比 HUD 热路径优化前后的资源加载量。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 只在性能基线需要量化的热路径上包一层计数；HUD 图标允许缺失，所以走 TryLoad。
    /// 用静态计数而不是每个对象各自统计，避免统计本身产生额外 GC。
    /// </summary>
    public static class ResourcesLoadTracker
    {
        private static long _totalCount;

        /// <summary>进程内累计的 HUD 资源加载次数。</summary>
        public static long TotalCount => _totalCount;

        /// <summary>
        /// 与旧 Resources.Load 调用点保持同样的缓存语义，额外累计一次调用。
        /// </summary>
        public static T Load<T>(string path) where T : Object
        {
            _totalCount++;
            return AssetService.TryLoad<T>(path);
        }

        /// <summary>基准测试开始前清零，避免把场景初始化的加载算进采样窗口。</summary>
        public static void ResetCounter()
        {
            _totalCount = 0;
        }
    }
}
