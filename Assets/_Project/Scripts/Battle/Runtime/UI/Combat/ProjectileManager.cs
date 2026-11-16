/****************************************************
    文件：ProjectileManager.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 17:00:00
    功能：追踪弹道对象池：Prefab 只加载一次，离屏弹道回收复用，避免每发 Instantiate/Destroy。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 弹道的创建与回收入口。池对象在切场景时会被 Unity 销毁，
    /// 这里每次取用都会跳过已销毁的引用，不需要额外的场景卸载回调。
    /// </summary>
    public static class ProjectileManager
    {
        private static ProjectileView _prefab;
        private static readonly Stack<ProjectileView> Pool = new Stack<ProjectileView>(16);

        /// <summary>本次运行累计创建的弹道数量，用于确认对象池是否真正命中。</summary>
        public static int CreatedCount { get; private set; }

        /// <summary>当前池中可复用的弹道数量。</summary>
        public static int PooledCount => Pool.Count;

        /// <summary>性能采样开始时清零，保证统计只覆盖本局。</summary>
        public static void ResetStats()
        {
            CreatedCount = 0;
        }

        /// <summary>战斗开始前预创建弹道，避免第一次远程攻击时 Instantiate。</summary>
        public static void Prewarm(int count)
        {
            if (!EnsurePrefab())
            {
                return;
            }

            PrunePool();
            while (Pool.Count < count)
            {
                ProjectileView projectile = Object.Instantiate(_prefab);
                projectile.gameObject.SetActive(false);
                Pool.Push(projectile);
            }
        }

        private static void PrunePool()
        {
            if (Pool.Count == 0)
            {
                return;
            }

            var alive = new List<ProjectileView>();
            while (Pool.Count > 0)
            {
                ProjectileView projectile = Pool.Pop();
                if (projectile != null)
                {
                    alive.Add(projectile);
                }
            }

            for (int i = alive.Count - 1; i >= 0; i--)
            {
                Pool.Push(alive[i]);
            }
        }

        public static void Spawn(
            CombatEntity owner,
            CombatEntity target,
            float speed)
        {
            Spawn(owner, target, speed, Color.white);
        }

        public static void Spawn(
            CombatEntity owner,
            CombatEntity target,
            float speed,
            Color tint)
        {
            if (owner == null || target == null)
            {
                return;
            }

            if (!EnsurePrefab())
            {
                return;
            }

            ProjectileView projectile = Rent();
            if (projectile == null)
            {
                return;
            }

            projectile.Launch(owner, target, speed, tint);
        }

        /// <summary>
        /// 命中或目标失效时由 ProjectileView 调用，回收而不是销毁。
        /// </summary>
        public static void Release(ProjectileView projectile)
        {
            if (projectile == null)
            {
                return;
            }

            projectile.ResetForPool();
            projectile.gameObject.SetActive(false);
            Pool.Push(projectile);
        }

        private static bool EnsurePrefab()
        {
            if (_prefab != null)
            {
                return true;
            }

            GameObject prefab = AssetService.Load<GameObject>("Projectile");
            if (prefab == null)
            {
                Debug.LogWarning("找不到 Assets/Resources/Projectile.prefab。");
                return false;
            }

            _prefab = prefab.GetComponent<ProjectileView>();
            return _prefab != null;
        }

        private static ProjectileView Rent()
        {
            while (Pool.Count > 0)
            {
                ProjectileView pooled = Pool.Pop();
                if (pooled == null)
                {
                    continue;
                }

                pooled.gameObject.SetActive(true);
                return pooled;
            }

            CreatedCount++;
            return Object.Instantiate(_prefab);
        }
    }
}
