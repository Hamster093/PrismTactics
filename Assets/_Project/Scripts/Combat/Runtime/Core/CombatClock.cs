/****************************************************
    文件：CombatClock.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：把渲染帧累积转换为固定 30Hz 战斗逻辑 tick。
*****************************************************/

using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 固定步长战斗时钟：把渲染帧累积转换为 30Hz 逻辑 tick。
    /// </summary>
    public sealed class CombatClock
    {
        private float _accumulator;// 时间累加器（秒），跨帧保留不足一个 tick 的余数，防止逻辑时间漂移

        public int TickCount { get; private set; }// 已推进的逻辑 tick 总数，仅由 AdvanceOneTick 递增

        public int Accumulate(float deltaTime)// 累积一帧时长并返回本帧应执行的 tick 数，含掉帧上限保护
        {
            if (deltaTime <= 0f)
            {
                return 0;
            }

            float maxFrameTime = CombatConstants.TickSeconds * CombatConstants.MaxTicksPerFrame;
            _accumulator += Mathf.Min(deltaTime, maxFrameTime);

            // 从累加器中按固定步长切分出完整 tick
            int ticks = 0;
            while (_accumulator >= CombatConstants.TickSeconds && ticks < CombatConstants.MaxTicksPerFrame)
            {
                _accumulator -= CombatConstants.TickSeconds;
                ticks++;
            }

            return ticks;
        }
        // 推进一个逻辑 tick，递增 TickCount
        public void AdvanceOneTick()
        {
            TickCount++;
        }
        // 重置累加器与 tick 计数，用于战斗开始/重演
        public void Reset()
        {
            _accumulator = 0f;
            TickCount = 0;
        }
    }
}
