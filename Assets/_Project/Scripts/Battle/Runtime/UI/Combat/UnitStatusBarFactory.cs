/****************************************************
    文件：UnitStatusBarFactory.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
    功能：给运行时单位挂血条/技力条和伤害反馈；血条 Prefab 只加载一次并池化复用。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public static class UnitStatusBarFactory
    {
        private static GameObject _barPrefab;
        private static Vector3 _barPrefabScale = Vector3.one;
        private static readonly Stack<GameObject> BarPool = new Stack<GameObject>(32);

        /// <summary>本次运行累计创建的血条数量，用于确认对象池命中率。</summary>
        public static int CreatedCount { get; private set; }

        /// <summary>当前池中可复用的血条数量。</summary>
        public static int PooledCount => BarPool.Count;

        /// <summary>性能采样开始时清零，保证统计只覆盖本局。</summary>
        public static void ResetStats()
        {
            CreatedCount = 0;
        }

        /// <summary>
        /// 战斗开始前预创建血条，避免第一波敌人集中出生时 Instantiate 造成 GC 尖峰。
        /// 预创建的对象和普通池对象完全一样，只是提前做好。
        /// </summary>
        public static void Prewarm(int count)
        {
            EnsurePrefab();
            if (_barPrefab == null)
            {
                return;
            }

            PrunePool();
            while (BarPool.Count < count)
            {
                GameObject bar = Object.Instantiate(_barPrefab);
                bar.name = "StatusBars";
                bar.SetActive(false);
                BarPool.Push(bar);
            }
        }

        private static void PrunePool()
        {
            if (BarPool.Count == 0)
            {
                return;
            }

            var alive = new List<GameObject>();
            while (BarPool.Count > 0)
            {
                GameObject bar = BarPool.Pop();
                if (bar != null)
                {
                    alive.Add(bar);
                }
            }

            for (int i = alive.Count - 1; i >= 0; i--)
            {
                BarPool.Push(alive[i]);
            }
        }

        public static void Attach(Transform unitRoot, CombatEntity entity)
        {
            if (unitRoot == null || entity == null)
            {
                return;
            }

            EnsurePrefab();

            Transform barTransform = unitRoot.Find("StatusBars");
            GameObject bar = barTransform != null ? barTransform.gameObject : null;
            if (bar == null && _barPrefab != null)
            {
                bar = Rent();
                if (bar != null)
                {
                    bar.name = "StatusBars";
                    bar.transform.SetParent(unitRoot, false);
                    bar.transform.localPosition = Vector3.zero;
                    bar.transform.localRotation = Quaternion.identity;
                    bar.transform.localScale = _barPrefabScale;

                    Camera camera = Camera.main;
                    if (camera != null)
                    {
                        // 相机和干员都不移动，只在创建时朝向相机，避免每帧 Billboard。
                        bar.transform.rotation = camera.transform.rotation;
                    }
                }
            }

            if (bar != null)
            {
                // 对象池复用时血条可能还在（敌人根节点一起回收），这里重新绑定新实体。
                HealthBarView health = bar.GetComponent<HealthBarView>();
                if (health != null)
                {
                    // 敌人满血不显示血条，第一次受伤才出现；干员保持常显。
                    health.Bind(entity, entity.Faction == Faction.Enemy);
                }

                SpCostBarView sp = bar.GetComponent<SpCostBarView>();
                if (sp != null)
                {
                    sp.Bind(entity);
                }
            }

            CombatFeedback feedback = unitRoot.GetComponent<CombatFeedback>();
            if (feedback == null)
            {
                feedback = unitRoot.gameObject.AddComponent<CombatFeedback>();
            }

            feedback.Initialize(entity, DamageNumberManager.Ensure());
        }

        /// <summary>
        /// 单位销毁前调用：解除事件绑定并把血条放回池，避免每个敌人都 Instantiate/Destroy 一套 Sprite。
        /// </summary>
        public static void Release(Transform unitRoot)
        {
            if (unitRoot == null)
            {
                return;
            }

            Transform bar = unitRoot.Find("StatusBars");
            if (bar == null)
            {
                return;
            }

            HealthBarView health = bar.GetComponent<HealthBarView>();
            if (health != null)
            {
                health.Unbind();
            }

            SpCostBarView sp = bar.GetComponent<SpCostBarView>();
            if (sp != null)
            {
                sp.Unbind();
            }

            bar.SetParent(null, false);
            bar.gameObject.SetActive(false);
            BarPool.Push(bar.gameObject);
        }

        private static void EnsurePrefab()
        {
            if (_barPrefab != null)
            {
                return;
            }

            _barPrefab = AssetService.Load<GameObject>("HealthBar");
            if (_barPrefab != null)
            {
                _barPrefabScale = _barPrefab.transform.localScale;
            }
        }

        private static GameObject Rent()
        {
            while (BarPool.Count > 0)
            {
                GameObject pooled = BarPool.Pop();
                if (pooled != null)
                {
                    pooled.SetActive(true);
                    return pooled;
                }
            }

            CreatedCount++;
            return Object.Instantiate(_barPrefab);
        }
    }
}
