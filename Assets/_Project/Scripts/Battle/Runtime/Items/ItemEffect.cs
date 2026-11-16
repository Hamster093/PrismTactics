/****************************************************
    文件：ItemEffect.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/09 17:20:00
	功能：战斗道具效果接口、上下文与注册表，支持 AOT 内置效果和热更 DLL 注册新效果。
*****************************************************/

using System;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>道具效果处理器：热更侧实现这个接口并注册即可扩展新道具。</summary>
    public interface IItemEffectHandler
    {
        string EffectKey { get; }
        bool Execute(ItemEffectContext context);
    }

    /// <summary>
    /// 道具效果上下文：把放置格、网格查询和治疗/飘字能力暴露给效果处理器，
    /// 处理器不直接持有 BattleController，便于热更侧只依赖稳定接口。
    /// </summary>
    public sealed class ItemEffectContext
    {
        private readonly BattleController _controller;
        private readonly List<CombatEntity> _allies = new List<CombatEntity>();

        public ItemEffectContext(BattleController controller, ItemDefinition item, Vector2Int cell)
        {
            _controller = controller;
            Item = item;
            Cell = cell;
        }

        public ItemDefinition Item { get; }
        public Vector2Int Cell { get; }
        public BattleGrid Grid => _controller != null ? _controller.Grid : null;

        /// <summary>收集以放置格为中心的方形范围内的友方单位（含中心格）。</summary>
        public IReadOnlyList<CombatEntity> AlliesInSquare(int radius)
        {
            _allies.Clear();
            if (_controller == null || Grid == null)
            {
                return _allies;
            }

            radius = Mathf.Max(0, radius);
            for (int x = Cell.x - radius; x <= Cell.x + radius; x++)
            {
                for (int y = Cell.y - radius; y <= Cell.y + radius; y++)
                {
                    CombatEntity ally = Grid.GetAllyAt(new Vector2Int(x, y));
                    if (ally != null && ally.IsAlive)
                    {
                        _allies.Add(ally);
                    }
                }
            }

            return _allies;
        }

        /// <summary>对目标治疗指定数值。</summary>
        public void Heal(CombatEntity target, float amount)
        {
            if (target != null && target.Health != null && amount > 0f)
            {
                target.Health.Heal(amount);
            }
        }

        /// <summary>预留接口：区域减速（泥沼）后续由 AOT 侧实现，当前只记录调用并返回失败。</summary>
        public bool AddSlowZone(Vector2Int cell, int radius, float slowPercent, float durationSeconds)
        {
            Debug.LogWarning("[Item] 区域减速尚未接入，effectKey="
                             + (Item != null ? Item.effectKey : string.Empty));
            return false;
        }

        /// <summary>在单位头顶生成一条飘字。</summary>
        public void ShowFloatingText(CombatEntity target, string content, Color color)
        {
            if (target != null)
            {
                ShowFloatingText(target.transform.position, content, color);
            }
        }

        /// <summary>在指定世界坐标生成一条飘字。</summary>
        public void ShowFloatingText(Vector3 worldPosition, string content, Color color)
        {
            DamageNumberManager.Ensure().Spawn(worldPosition, content, color);
        }
    }

    /// <summary>
    /// 道具效果注册表：effectKey → 处理器。
    /// AOT 内置处理器在静态构造里注册，热更 DLL 在 OnRegister 里追加注册，同 key 覆盖。
    /// </summary>
    public static class ItemEffectRegistry
    {
        private static readonly Dictionary<string, IItemEffectHandler> Handlers =
            new Dictionary<string, IItemEffectHandler>(StringComparer.Ordinal);

        static ItemEffectRegistry()
        {
            RegisterBuiltins();
        }

        /// <summary>注册道具效果处理器，空 key 静默忽略，同 key 覆盖。</summary>
        public static void Register(IItemEffectHandler handler)
        {
            if (handler != null && !string.IsNullOrEmpty(handler.EffectKey))
            {
                Handlers[handler.EffectKey] = handler;
            }
        }

        /// <summary>按 item.effectKey 派发；未注册或处理器返回 false 时返回 false。</summary>
        public static bool TryExecute(ItemEffectContext context)
        {
            return context != null
                   && context.Item != null
                   && !string.IsNullOrEmpty(context.Item.effectKey)
                   && Handlers.TryGetValue(context.Item.effectKey, out IItemEffectHandler handler)
                   && handler.Execute(context);
        }

        /// <summary>热更卸载/失败时清空外部处理器，并恢复 AOT 内置处理器。</summary>
        public static void Reset()
        {
            Handlers.Clear();
            RegisterBuiltins();
        }

        /// <summary>注册主包自带的效果处理器。</summary>
        private static void RegisterBuiltins()
        {
            Register(new HealAreaItemEffectHandler());
        }
    }

    /// <summary>AOT 内置效果：以放置格为中心，范围内友方按最大生命百分比回血。</summary>
    public sealed class HealAreaItemEffectHandler : IItemEffectHandler
    {
        public string EffectKey => "heal_area";

        public bool Execute(ItemEffectContext context)
        {
            if (context == null)
            {
                return false;
            }

            IReadOnlyList<CombatEntity> allies = context.AlliesInSquare(context.Item.radius);
            if (allies.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < allies.Count; i++)
            {
                CombatEntity ally = allies[i];
                float amount = ally.MaxHealth * context.Item.healPercent;
                context.Heal(ally, amount);
                context.ShowFloatingText(ally, "+" + Mathf.RoundToInt(amount), new Color(0.35f, 1f, 0.45f));
            }

            return true;
        }
    }
}
