/****************************************************
    文件：HotUpdateSkillApi.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/03 0:04:00
	功能：战斗系统 - 技能热更桥接接口层 (AOT 侧)
          提供热更代码所需的所有“原语”能力，确保热更只负责逻辑组合，
          不产生底层依赖和额外GC。
*****************************************************/
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    /// <summary>
    /// 【热更技能入口接口】
    /// 所有的具体技能逻辑都在热更 DLL 里实现此接口。
    /// AOT 层通过读取配置表中的 EffectKey，反射或字典查找到对应的 Handler 并执行。
    /// </summary>
    public interface ISkillEffectHandler
    {
        string EffectKey { get; }// 技能的唯一标识符
        bool Execute(ISkillEffectContext context);// 执行技能逻辑。
    }
    /// <summary>
    /// 【技能执行上下文】
    ///  AOT 层递给热更层的“万能工具箱”。
    /// 热更代码只能通过这里暴露的方法与游戏世界交互，保证底层性能和生命周期可控。
    /// </summary>
    public interface ISkillEffectContext
    {
        // ================= 基础信息 =================
        /// <summary>技能释放者</summary>
        CombatEntity Owner { get; }
        /// <summary>技能配置数据（包含数值、CD、耗蓝等基础配置）</summary>
        SkillConfig Skill { get; }
        /// <summary>当前战斗逻辑帧（用于计算延迟、Buff持续时间等）</summary>
        int CurrentTick { get; }

        // ================= 目标查询 (空间计算) =================
        /// <summary>
        /// 获取施法者当前攻击范围内的所有敌人（基于施法者自身的 Range 属性）
        /// </summary>
        IReadOnlyList<CombatEntity> EnemiesInRange();
        /// <summary>
        /// 获取指定球体范围内的所有敌人（用于 AOE 爆炸、弹幕碰撞检测）
        /// </summary>
        IReadOnlyList<CombatEntity> EnemiesInRadius(Vector3 center, float radius);

        void DealDamage(CombatEntity target, float rawDamage, DamageType type);// 造成伤害。传入的是 rawDamage（基础伤害），AOT 底层会自动处理防御减伤、暴击、闪避等复杂计算
        void Heal(CombatEntity target, float amount);// 治疗目标。

        // ================= 定时器 (驱动热更逻辑) =================
        void Schedule(int delayTicks, Action action);// 延迟执行一次。
        void ScheduleRepeating(int intervalTicks, int count, Action action);// 循环执行（定时器）。用于：DoT（持续伤害）跳伤、弹幕每帧移动、Buff 持续扣血等。

        // ================= 运行时属性修改 (Buff/开关型技能) =================
        void SetRangeOverride(Vector2Int[] cells, int durationTicks);// 临时覆盖攻击范围（改变当前攻击网格）
        void AddAttackSpeedPercent(float percent, int durationTicks);// 增加攻击速度百分比（例如 0.5f 代表增加 50%）
        void AddStatModifier(StatType stat, float flat, float percent, int durationTicks);// 增加基础属性修改器（如加攻击力、加护甲，支持固定值和百分比）
        
        // ================= 视觉表现 (解耦与防GC核心) =================
        GameObject SpawnVisual(string address, Vector3 position, Quaternion rotation);// 生成视觉对象（如子弹模型、爆炸特效）。
        void DespawnVisual(GameObject visual);// 回收视觉对象（归还给对象池，而不是 Destroy 销毁）。
    }

    /// <summary>
    /// 【热更视觉加载服务接口】
    /// 隔离热更层与底层资源加载系统。
    /// 热更层不知道资源是怎么加载的，只通过字符串 Address 索要。
    /// </summary>
    public interface IHotUpdateVisualService
    {
        GameObject Spawn(string address, Vector3 position, Quaternion rotation);
        void Despawn(GameObject visual);
    }
    /// <summary>
    /// 【热更视觉服务静态桥接器】
    /// 因为热更层无法直接引用 AOT 中的实例引用，所以用一个静态类作为中转站。
    /// AOT 游戏启动时（如 GameManager 初始化），会把实现了 IHotUpdateVisualService 的实例赋值给 Instance。
    /// 之后热更代码就可以直接通过 HotUpdateVisualService.Spawn(...) 来生成特效了。
    /// </summary>
    public static class HotUpdateVisualService
    {
        public static IHotUpdateVisualService Instance { get; set; }// 由 AOT 层在游戏启动时注入实际逻辑

        public static GameObject Spawn(string address, Vector3 position, Quaternion rotation)
        {
            if (Instance == null)
            {
                Debug.LogError("[HotUpdate] IHotUpdateVisualService 未注入，无法生成：" + address);
                return null;
            }

            return Instance.Spawn(address, position, rotation);
        }
    }
}