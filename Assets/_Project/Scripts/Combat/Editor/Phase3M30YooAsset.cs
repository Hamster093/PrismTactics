/****************************************************
    文件：Phase3M30YooAsset.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/31 15:38:00
    功能：M3.1 配置收集：维护 BattleRes/HotUpdateRes 资源包，并校验收集与模拟构建结果。
*****************************************************/

using System;
using System.IO;
using System.Linq;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 配置统一放在 GameContent/Configs，由 Collector 收进 BattleRes。
    /// 运行时加载由 BattleContentBootstrap 负责；本工具只做编辑器侧的收集与校验。
    /// </summary>
    public static class Phase3M30YooAsset
    {
        /// <summary>战斗资源包：配置、UI、单位、地图、战斗表现都放这里。</summary>
        public const string BattleResPackage = "BattleRes";

        /// <summary>热更资源包：热更 DLL 与 AOT 元数据。</summary>
        public const string HotUpdateResPackage = "HotUpdateRes";

        private const string GameContentRoot = "Assets/GameContent";
        private const string ConfigsRoot = GameContentRoot + "/Configs";
        private const string ConfigsGroup = "Configs";
        private const string RequiredConfigFile = "levels.json";

        // ------------------------------------------------------------------ 菜单

        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.1 初始化 Collector", false, 20)]
        public static void SetupFromMenu()
        {
            Setup();
        }

        [MenuItem("Tools/PrismTactics/9 其他/9.4 初始化/9.4.3 校验配置收集", false, 21)]
        public static void ValidateFromMenu()
        {
            Validate();
        }

        // ------------------------------------------------------------ 命令行入口

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3M30YooAsset.SetupFromCli</summary>
        public static void SetupFromCli()
        {
            RunCli("同步配置并初始化 Collector", Setup);
        }

        /// <summary>命令行入口：-executeMethod PrismTactics.EditorTools.Phase3M30YooAsset.ValidateFromCli</summary>
        public static void ValidateFromCli()
        {
            RunCli("校验配置收集", Validate);
        }

        // ------------------------------------------------------------------ 实现

        /// <summary>
        /// 幂等初始化：确保 BattleRes/HotUpdateRes 两个包和 Configs 组存在。
        /// </summary>
        public static void Setup()
        {
            EnsureCollectorSetting();
            Debug.Log("[M3.1] Collector 初始化完成。下一步：执行“M3.1 校验配置收集与模拟构建”。");
        }

        /// <summary>
        /// 收集 BattleRes 并跑一次 EditorSimulateBuild，验证“地址 → 资源包 → 清单”链路。
        /// 真正的运行时加载由 PlayMode 测试 YooAssetCollectorTests 覆盖。
        /// </summary>
        public static void Validate()
        {
            EnsureCollectorSetting();

            AssetBundleCollectorSetting setting = SettingLoader.LoadSettingData<AssetBundleCollectorSetting>();
            Debug.Log($"[M3.1] Collector 配置：{AssetDatabase.GetAssetPath(setting)}，包数量 = {setting.Packages.Count}");

            string[] sourceFiles = Directory.GetFiles(
                BattleConfigLoader.ConfigDirectory, "*.json", SearchOption.AllDirectories);
            Debug.Log($"[M3.1] 配置源文件 {sourceFiles.Length} 个");

            // 1) 收集结果
            CollectResult collectResult = setting.BeginCollect(BattleResPackage, true, true);
            Debug.Log($"[M3.1] BattleRes 收集到 {collectResult.CollectAssets.Count} 个资源：");
            foreach (CollectAssetInfo asset in collectResult.CollectAssets)
            {
                Debug.Log($"[M3.1]   address={asset.Address} bundle={asset.BundleName} path={asset.AssetInfo.AssetPath}");
            }

            if (collectResult.CollectAssets.Count != sourceFiles.Length)
            {
                throw new Exception(
                    $"收集数量与配置源不一致：收集 {collectResult.CollectAssets.Count} 个，源 {sourceFiles.Length} 个");
            }

            if (!collectResult.CollectAssets.Any(
                    a => a.AssetInfo.AssetPath.EndsWith("/" + RequiredConfigFile, StringComparison.Ordinal)))
                throw new Exception($"收集结果里没有 {RequiredConfigFile}");

            if (!collectResult.CollectAssets.Any(a => a.Address == "levels"))
                throw new Exception("收集结果里没有 levels 地址（AddressByFileName 规则失效？）");

            // 2) 模拟构建出清单
            PackageInvokeBuildResult buildResult = EditorSimulateModeHelper.SimulateBuild(BattleResPackage);
            if (buildResult == null || string.IsNullOrEmpty(buildResult.PackageRootDirectory))
                throw new Exception("EditorSimulateBuild 没有返回输出目录");
            if (!Directory.Exists(buildResult.PackageRootDirectory))
                throw new Exception($"EditorSimulateBuild 输出目录不存在：{buildResult.PackageRootDirectory}");

            Debug.Log($"[M3.1] 模拟构建输出目录：{buildResult.PackageRootDirectory}");
            foreach (string file in Directory.GetFiles(buildResult.PackageRootDirectory))
            {
                Debug.Log($"[M3.1]   产物：{Path.GetFileName(file)}（{new FileInfo(file).Length:N0} B）");
            }

            bool hasManifest = Directory.GetFiles(buildResult.PackageRootDirectory, "*.bytes")
                .Any(f => Path.GetFileName(f).StartsWith(BattleResPackage + "_", StringComparison.Ordinal));
            if (!hasManifest)
                throw new Exception($"模拟构建输出里没有 {BattleResPackage}_*.bytes 清单");

            Debug.Log($"[M3.1] 校验通过：{collectResult.CollectAssets.Count} 个配置已进入 BattleRes，levels 地址可用，清单已生成。");
        }

        /// <summary>
        /// 创建/校正两个 YooAsset 资源包。
        /// 注意：HotUpdateRes 在 M3.3 之前没有资源，这里只建包、不建组，避免空收集器报错。
        /// </summary>
        private static void EnsureCollectorSetting()
        {
            AssetBundleCollectorSetting setting = SettingLoader.LoadSettingData<AssetBundleCollectorSetting>();

            AssetBundleCollectorPackage battleRes = setting.Packages
                .FirstOrDefault(p => p.PackageName == BattleResPackage);
            if (battleRes == null)
            {
                battleRes = new AssetBundleCollectorPackage
                {
                    PackageName = BattleResPackage,
                    PackageDesc = "战斗资源：配置、UI、单位、地图、战斗表现",
                };
                setting.Packages.Add(battleRes);
            }
            battleRes.EnableAddressable = true;
            battleRes.SupportExtensionless = true;
            battleRes.AutoCollectShaders = true;

            AssetBundleCollectorGroup configsGroup = battleRes.Groups
                .FirstOrDefault(g => g.GroupName == ConfigsGroup);
            if (configsGroup == null)
            {
                configsGroup = new AssetBundleCollectorGroup
                {
                    GroupName = ConfigsGroup,
                    GroupDesc = "配置 JSON（GameContent）",
                };
                battleRes.Groups.Add(configsGroup);
            }

            AssetBundleCollector collector = configsGroup.Collectors
                .FirstOrDefault(c => c.CollectPath == ConfigsRoot);
            if (collector == null)
            {
                collector = new AssetBundleCollector
                {
                    CollectPath = ConfigsRoot,
                    CollectorType = ECollectorType.MainAssetCollector,
                    AddressRuleName = nameof(AddressByFileName),
                    PackRuleName = nameof(PackDirectory),
                    FilterRuleName = nameof(CollectAll),
                };
                configsGroup.Collectors.Add(collector);
            }
            collector.CollectorGUID = AssetDatabase.AssetPathToGUID(ConfigsRoot);
            collector.CollectorType = ECollectorType.MainAssetCollector;
            collector.AddressRuleName = nameof(AddressByFileName);
            collector.PackRuleName = nameof(PackDirectory);
            collector.FilterRuleName = nameof(CollectAll);

            if (setting.Packages.All(p => p.PackageName != HotUpdateResPackage))
            {
                setting.Packages.Add(new AssetBundleCollectorPackage
                {
                    PackageName = HotUpdateResPackage,
                    PackageDesc = "热更资源：热更 DLL 与 AOT 元数据（M3.3 接入）",
                    EnableAddressable = true,
                });
            }

            setting.CheckAllPackageConfigError();

            EditorUtility.SetDirty(setting);
            AssetDatabase.SaveAssets();
        }

        private static void RunCli(string actionName, Action action)
        {
            int exitCode = 0;
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogError($"[M3.1] {actionName} 失败：{exception}");
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
            else if (exitCode != 0)
            {
                throw new Exception($"[M3.1] {actionName} 失败，详见 Console。");
            }
        }
    }
}
