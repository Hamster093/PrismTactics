/****************************************************
    文件：CombatHotUpdateRegistry.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/03 0:21:00
	功能：技能效果热更新注册表，允许在运行时注册和执行技能效果处理器。
*****************************************************/

using PrismTactics.Combat;
using System;
using System.Collections.Generic;
namespace PrismTactics.Combat
{
    /// <summary>
    /// 技能效果热更新注册表：维护 "effectKey → 处理器" 的全局映射，供战斗系统按 key 派发技能效果。
    ///
    /// 核心机制（注册表 / 策略模式）：
    ///   热更 DLL 在 OnRegister 阶段调用 Register 注册一批 ISkillEffectHandler；
    ///   战斗运行时用 TryExecute 按 Skill.effectKey 查表并执行对应处理器。
    ///
    /// 职责边界：
    /// - 本类只管"存 handler"和"按 key 派发"，不关心 handler 内部逻辑；
    /// - effectKey 的来源（技能配置）由外部负责，本类不校验业务合法性；
    /// - 热更加载失败时由 HotUpdateRuntime 触发 Reset，本类不主动感知加载状态。
    ///
    /// 设计取舍：
    /// - 静态类 + 静态字典：进程级单例，热更场景下简单直接，无需注入；
    /// - 字符串比较用 Ordinal：effectKey 是程序内标识符而非用户输入，序数比较最快且不受区域设置影响；
    /// - 注册采用"覆盖"而非"追加"：同一 key 只保留最新注册，避免重复注册导致效果重复执行；
    /// - 非法注册（null / 空 key）静默忽略：单个 handler 配置错误不应中断整体热更流程。
    /// </summary>
    public static class CombatHotUpdateRegistry
    {
        #region 私有状态字段
        /// <summary>
        /// effectKey → 处理器 的全局映射。
        /// 注：静态可变状态，进程级存活、跨场景保留；热更卸载/失败时必须显式调用 Reset，否则旧 handler 会残留。
        /// 使用 StringComparer.Ordinal 保证大小写敏感、与区域设置无关的确定性比较。
        /// </summary>
        private static readonly Dictionary<string, ISkillEffectHandler> Handlers =
            new Dictionary<string, ISkillEffectHandler>(StringComparer.Ordinal);
        #endregion

        #region 公开接口
        /// <summary>
        /// 注册技能效果处理器。
        /// 前置条件：handler 非 null 且 EffectKey 非空，否则静默忽略（不抛异常、不报错）。
        /// 副作用：写入全局 Handlers；相同 EffectKey 会覆盖已有处理器（覆盖语义，非追加）。
        /// 设计动机：覆盖而非追加，保证同一 effectKey 只有一个生效实现，热更重复注册不会导致重复执行。
        /// </summary>
        public static void Register(ISkillEffectHandler handler)
        {
            if (handler != null && !string.IsNullOrEmpty(handler.EffectKey))
            {
                Handlers[handler.EffectKey] = handler;
            }
        }
        /// <summary>
        /// 按 context.Skill.effectKey 查找并执行对应处理器。
        /// 返回 false 的情况：context 为 null、effectKey 为空、未注册对应 handler、或 handler.Execute 本身返回 false。
        /// 副作用：会执行 handler.Execute(context)，具体副作用由 handler 决定。
        /// 注意：短路求值（&&），任一前置检查失败都不会继续执行后续步骤。
        /// 潜在坑：context != null 已检查，但 context.Skill 未做 null 检查；若 Skill 为 null 会抛 NullReferenceException。
        /// </summary>
        public static bool TryExecute(ISkillEffectContext context)
        {
            return context != null
                   && !string.IsNullOrEmpty(context.Skill.effectKey)
                   && Handlers.TryGetValue(context.Skill.effectKey, out ISkillEffectHandler handler)
                   && handler.Execute(context);
        }
        #endregion
        #region 清理
        /// <summary>
        /// 清空所有已注册处理器。
        /// 配对：与 Register 配对；热更加载失败、热更重置时必须调用，避免旧 handler 残留导致后续派发命中过期实现。
        /// </summary>
        public static void Reset() => Handlers.Clear();
        #endregion
    }
}