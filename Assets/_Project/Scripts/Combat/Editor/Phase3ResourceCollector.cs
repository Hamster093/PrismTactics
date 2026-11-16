/****************************************************
    文件：Phase3ResourceCollector.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/2 14:10:00
    功能：M3.1 资源 Collector 补全：把 UI、战斗表现、地图/单位目录加入 BattleRes。
*****************************************************/

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 幂等补全资源收集器；地址统一用 GameContentPathAddressRule，保持旧 Resources 路径。
    /// </summary>
    public static class Phase3ResourceCollector
    {
        private const string PackageName = "BattleRes";

        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.2 补全资源 Collector", false, 30)]
        public static void EnsureFromMenu()
        {
            Ensure();
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3ResourceCollector.EnsureFromCli</summary>
        public static void EnsureFromCli()
        {
            int exitCode = 0;
            try
            {
                Ensure();
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.1] 补全资源 Collector 失败：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        public static void Ensure()
        {
            AssetBundleCollectorSetting setting = SettingLoader.LoadSettingData<AssetBundleCollectorSetting>();
            AssetBundleCollectorPackage package = setting.Packages
                .FirstOrDefault(p => p.PackageName == PackageName);
            if (package == null)
            {
                throw new Exception("找不到资源包：" + PackageName);
            }

            EnsureGroup(package, "CombatVFX", new[]
            {
                "Assets/GameContent/CombatBars",
                "Assets/GameContent/DamageNumber.prefab",
                "Assets/GameContent/HealthBar.prefab",
                "Assets/GameContent/Projectile.prefab",
            });

            EnsureGroup(package, "UI", new[]
            {
                "Assets/GameContent/UIPrefab",
                "Assets/GameContent/UIHandDrawn",
            });

            EnsureGroup(package, "MapVisual", new[]
            {
                "Assets/GameContent/MapVisual",
                "Assets/GameContent/MapVisualConfig.asset",
                "Assets/GameContent/UnitPrefabCatalog.asset",
            });

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
            Debug.Log("[M3.1] 资源 Collector 已补全。");
        }

        private static void EnsureGroup(AssetBundleCollectorPackage package, string groupName, string[] paths)
        {
            AssetBundleCollectorGroup group = package.Groups
                .FirstOrDefault(g => g.GroupName == groupName);
            if (group == null)
            {
                group = new AssetBundleCollectorGroup
                {
                    GroupName = groupName,
                    GroupDesc = "M3.1 资源迁移",
                };
                package.Groups.Add(group);
            }

            foreach (string path in paths)
            {
                if (!AssetDatabase.IsValidFolder(path)
                    && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                {
                    Debug.LogWarning("[M3.1] 收集路径不存在，已跳过：" + path);
                    continue;
                }

                AssetBundleCollector collector = group.Collectors
                    .FirstOrDefault(c => c.CollectPath == path);
                if (collector == null)
                {
                    collector = new AssetBundleCollector
                    {
                        CollectPath = path,
                        CollectorType = ECollectorType.MainAssetCollector,
                    };
                    group.Collectors.Add(collector);
                }

                collector.CollectorGUID = AssetDatabase.AssetPathToGUID(path);
                collector.CollectorType = ECollectorType.MainAssetCollector;
                collector.AddressRuleName = nameof(GameContentPathAddressRule);
                collector.PackRuleName = nameof(PackDirectory);
                collector.FilterRuleName = nameof(CollectAll);
            }
        }
    }
}
