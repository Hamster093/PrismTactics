/****************************************************
    文件：Phase3ContentBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 15:40:00
    功能：内容包构建：把 BattleRes 按指定版本打成 OfflinePlayMode 内置包，可保留旧版本供回滚。
*****************************************************/

using System;
using System.IO;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 只构建 BattleRes；release.json 由 Phase3ReleaseBuilder 维护。
    /// </summary>
    public static class Phase3ContentBuilder
    {
        public const string PackageName = "BattleRes";
        public const string DefaultVersion = "1.0.0";
        private const string PipelineName = "ScriptableBuildPipeline";

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.2 内容/2.2.1 构建 BattleRes", false, 27)]
        public static void BuildFromMenu()
        {
            string version = GetTargetVersion();
            BuildResult result = BuildContent(version, true);
            EditorUtility.DisplayDialog(
                "M3.1 内容包",
                result.Success
                    ? $"BattleRes {version} 构建完成：\n" + result.OutputPackageDirectory
                    : "构建失败：\n" + result.ErrorInfo,
                "好");
        }

        /// <summary>命令行入口：环境变量 PRISM_CONTENT_VERSION 指定版本（默认 release.latest）。</summary>
        public static void BuildFromCli()
        {
            int exitCode = 0;
            try
            {
                string version = GetTargetVersion();
                bool keepBuiltin = Environment.GetEnvironmentVariable("PRISM_CONTENT_KEEP_BUILTIN") != "0";
                BuildResult result = BuildContent(version, keepBuiltin);
                if (!result.Success)
                {
                    Debug.LogError("[M3.1] BattleRes 构建失败：" + result.ErrorInfo);
                    exitCode = 1;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.1] BattleRes 构建异常：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>版本优先级：环境变量 > release.latest > 1.0.0。</summary>
        public static string GetTargetVersion()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("PRISM_CONTENT_VERSION");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            ContentReleaseCatalog catalog = ContentReleaseStore.Load(out _);
            if (catalog != null && !string.IsNullOrWhiteSpace(catalog.latest))
            {
                return catalog.latest;
            }

            return DefaultVersion;
        }

        /// <summary>
        /// 构建并复制到 StreamingAssets/yoo/BattleRes，供 OfflinePlayMode 读取。
        /// keepBuiltin=true 时只追加文件，保留旧版本清单供回滚。
        /// </summary>
        public static BuildResult BuildContent(string version, bool keepBuiltin)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("内容版本不能为空", nameof(version));
            }

            var parameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = AssetBundleBuilderHelper.GetDefaultBuildOutputRoot(),
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = PipelineName,
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                PackageName = PackageName,
                PackageVersion = version,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = AssetBundleBuilderSetting.GetPackageFileNameStyle(PackageName, PipelineName),
                BuildinFileCopyOption = keepBuiltin
                    ? EBuildinFileCopyOption.OnlyCopyAll
                    : EBuildinFileCopyOption.ClearAndCopyAll,
                BuildinFileCopyParams = string.Empty,
                CompressOption = AssetBundleBuilderSetting.GetPackageCompressOption(PackageName, PipelineName),
                ClearBuildCacheFiles = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName),
                UseAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName),
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(),
            };

            // YooAsset 不允许复用上一次的版本输出目录；本地重打包先清掉本次版本目录。
            string versionOutput = parameters.GetPackageOutputDirectory();
            if (Directory.Exists(versionOutput))
            {
                Directory.Delete(versionOutput, true);
            }

            Debug.Log($"[M3.1] 开始构建 {PackageName} {version} ...");
            BuildResult result = new ScriptableBuildPipeline().Run(parameters, true);
            if (!result.Success)
            {
                Debug.LogError($"[M3.1] 构建失败：{result.ErrorInfo}");
                return result;
            }

            Debug.Log($"[M3.1] 构建产物：{result.OutputPackageDirectory}");
            string builtinRoot = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Assets/StreamingAssets/yoo",
                PackageName);
            string manifestFile = Path.Combine(builtinRoot, $"{PackageName}_{version}.bytes");
            if (!Directory.Exists(builtinRoot) || !File.Exists(manifestFile))
            {
                Debug.LogError($"[M3.1] 内置清单没有复制到：{manifestFile}");
                return result;
            }

            Debug.Log($"[M3.1] 内置文件：{builtinRoot}（{PackageName}_{version}.bytes）");
            Phase3BuildinCatalogMerger.Merge(builtinRoot);
            AssetDatabase.Refresh();
            return result;
        }

        private static string GetBuiltinShaderBundleName()
        {
            bool unique = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
            return DefaultPackRule.CreateShadersPackRuleResult().GetBundleName(PackageName, unique);
        }
    }
}
