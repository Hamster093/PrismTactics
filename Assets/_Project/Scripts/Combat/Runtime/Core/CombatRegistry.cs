/****************************************************
    文件：CombatRegistry.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：战斗实体注册表：注册、注销、逐 tick 更新与范围查询。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Combat
{
    // 战斗实体注册表：维护所有存活实体并逐 tick 更新
    public sealed class CombatRegistry
    {// 所有已注册实体
        private readonly List<CombatEntity> _entities = new List<CombatEntity>();
        // 只读实体列表
        public IReadOnlyList<CombatEntity> Entities => _entities;
        public int Count => _entities.Count; // 实体数量
        // 注册实体（去重）
        public void Register(CombatEntity entity)
        {
            if (entity != null && !_entities.Contains(entity))
            {
                _entities.Add(entity);
            }
        }
        // 注销实体
        public void Unregister(CombatEntity entity)
        {
            _entities.Remove(entity);
        }
        // 逐 tick 更新所有存活实体；倒序遍历以支持遍历中移除
        public void TickAll(int tick)
        {
            for (int i = _entities.Count - 1; i >= 0; i--)
            {
                CombatEntity entity = _entities[i];
                if (entity != null && entity.IsAlive)
                {
                    entity.Tick(tick);
                }
            }
        }
        // 收集以 center 为圆心、radius 为半径内的所有敌对存活实体
        public void GetEnemiesInRadius(Faction faction, Vector3 center, float radius, List<CombatEntity> results)
        {
            results.Clear();
            float radiusSquared = radius * radius;

            for (int i = 0; i < _entities.Count; i++)
            {
                CombatEntity entity = _entities[i];
                if (entity == null || !entity.IsAlive || entity.Faction == faction)
                {
                    continue;
                }

                if ((entity.TargetPosition - center).sqrMagnitude <= radiusSquared)
                {
                    results.Add(entity);
                }
            }
        }
    }
}
