/****************************************************
    文件：HotUpdateTickService.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/03 0:11:00
	功能：热更新侧的定时器/调度中心（AOT 驱动）。
          为热更代码提供延迟执行、重复执行（DoT、弹幕移动等）的能力，
          统一由 CombatWorld 的 Tick 驱动，严格管控生命周期，防止内存泄漏。
*****************************************************/
using System;
using System.Collections.Generic;

namespace PrismTactics.Combat
{
    public static class HotUpdateTickService
    {
        /// <summary>
        /// 定时任务条目。
        /// 使用 class 而不是 struct，避免存入 List 时产生装箱或频繁的内存拷贝。
        /// </summary>
        private sealed class Entry
        {
            public int NextTick;       // 下一次触发时的系统 Tick
            public int Interval;       // 触发间隔（仅用于重复任务）
            public int Remaining;      // 剩余执行次数
            public Action Action;      // 执行的回调
        }

        // 存放所有待执行的定时任务。使用静态 List 统一管理，方便在切场景时一键清空。
        private static readonly List<Entry> Entries = new List<Entry>();

        // 当前系统的逻辑帧（由外部 CombatWorld 每次 Tick 时传入）
        public static int CurrentTick { get; private set; }

        /// <summary>
        /// 延迟执行一次（单次定时器）
        /// </summary>
        /// <param name="delayTicks">延迟多少帧执行（基于 30Hz 逻辑帧，30 = 1秒）</param>
        /// <param name="action">要执行的回调</param>
        public static void Schedule(int delayTicks, Action action)
        {
            if (action == null) return;
            Entries.Add(new Entry
            {
                NextTick = CurrentTick + Math.Max(0, delayTicks),
                Remaining = 1, // 只执行一次
                Action = action,
            });
        }

        /// <summary>
        /// 重复执行（循环定时器）
        /// 【重点注意】此方法设计为：调用时立即执行第一次，然后再开始计时循环。
        /// </summary>
        /// <param name="intervalTicks">每次执行的间隔帧数</param>
        /// <param name="count">总共执行的次数（包含立即执行的这一次）</param>
        /// <param name="action">要执行的回调</param>
        public static void ScheduleRepeating(int intervalTicks, int count, Action action)
        {
            if (action == null || count <= 0) return;

            // 立即执行第一次（比如释放 DoT 技能时，第一跳伤害通常是立刻生效的）
            action();

            // 如果只需要执行一次，直接返回，不用放进列表了
            if (count <= 1) return;

            Entries.Add(new Entry
            {
                NextTick = CurrentTick + Math.Max(1, intervalTicks),
                Interval = Math.Max(1, intervalTicks),
                Remaining = count - 1, // 剩下的次数
                Action = action,
            });
        }

        /// <summary>
        /// 核心驱动方法：由 AOT 层的战斗循环每帧调用。
        /// </summary>
        /// <param name="tick">当前帧数</param>
        public static void Tick(int tick)
        {
            CurrentTick = tick;

            // 必须倒序遍历！
            // 因为如果在遍历过程中移除元素（Entries.RemoveAt），正序遍历会导致索引错乱跳过元素。
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry entry = Entries[i];

                // 还没到时间，跳过
                if (tick < entry.NextTick) continue;

                // 先移除，再执行。
                // 这样做的好处是：如果 entry.Action 内部又调用了 Schedule/ScheduleRepeating，
                // 新添加的任务会被加到列表末尾，不会影响当前的倒序循环（避免了死循环或并发修改异常）。
                Entries.RemoveAt(i);

                // 执行回调
                entry.Action?.Invoke();

                // 处理重复逻辑
                entry.Remaining--;
                if (entry.Remaining > 0)
                {
                    // 计算下一次执行的时间
                    entry.NextTick += entry.Interval;
                    // 重新塞回列表等待下一次执行
                    Entries.Add(entry);
                }
            }
        }

        /// <summary>
        /// 清空所有定时器。
        /// 【极其重要】必须在战斗结束、切换场景、或者对象池回收时调用，否则会导致严重的内存泄漏和空引用报错。
        /// </summary>
        public static void Clear()
        {
            Entries.Clear();
            CurrentTick = 0;
        }
    }
}
