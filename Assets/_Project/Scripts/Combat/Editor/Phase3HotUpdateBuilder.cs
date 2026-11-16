/****************************************************
    文件：Phase3HotUpdateBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/4 14:00:00
    功能：构建 HotUpdateRes 热更包：把热更 DLL 按指定版本打进 Offline 内置目录，并保留旧版本用于回滚。
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
    /// 只构建 HotUpdateRes；release.json 由 Phase3ReleaseBuilder 维护。
    /// </summary>
    public static class Phase3HotUpdateBuilder
    {
        public const string PackageName = "HotUpdateRes";
        private const string PipelineName = "ScriptableBuildPipeline";

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.1 热更/2.1.2 构建 HotUpdateRes", false, 30)]
        public static void BuildFromMenu()
        {
            string version = GetTargetVersion();
            BuildResult result = Build(version);
            EditorUtility.DisplayDialog(
                "M3.3 热更包",
                result.Success
                    ? $"HotUpdateRes {version} 构建完成：\n" + result.OutputPackageDirectory
                    : "构建失败：\n" + result.ErrorInfo,
                "好");
        }

        /// <summary>命令行入口：环境变量 PRISM_HOTUPDATE_VERSION 指定版本（默认 release.latest）。</summary>
        public static void BuildFromCli()
        {
            int exitCode = 0;
            try
            {
                Build(GetTargetVersion());
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.3] HotUpdateRes 构建失败：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        public static string GetTargetVersion()
        {
            string fromEnvironment = Environment.GetEnvironmentVariable("PRISM_HOTUPDATE_VERSION");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            return Phase3ContentBuilder.GetTargetVersion();
        }

        public static BuildResult Build(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new ArgumentException("热更包版本不能为空", nameof(version));
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
                BuildinFileCopyOption = EBuildinFileCopyOption.OnlyCopyAll,
                BuildinFileCopyParams = string.Empty,
                CompressOption = AssetBundleBuilderSetting.GetPackageCompressOption(PackageName, PipelineName),
                ClearBuildCacheFiles = AssetBundleBuilderSetting.GetPackageClearBuildCache(PackageName, PipelineName),
                UseAssetDependencyDB = AssetBundleBuilderSetting.GetPackageUseAssetDependencyDB(PackageName, PipelineName),
                BuiltinShadersBundleName = GetBuiltinShaderBundleName(),
            };

            string versionOutput = parameters.GetPackageOutputDirectory();
            if (Directory.Exists(versionOutput))
            {
                Directory.Delete(versionOutput, true);
            }

            Debug.Log($"[M3.3] 开始构建 {PackageName} {version} ...");
            BuildResult result = new ScriptableBuildPipeline().Run(parameters, true);
            if (!result.Success)
            {
                Debug.LogError($"[M3.3] 构建失败：{result.ErrorInfo}");
                return result;
            }

            string builtinRoot = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Assets/StreamingAssets/yoo",
                PackageName);
            string manifestFile = Path.Combine(builtinRoot, $"{PackageName}_{version}.bytes");
            if (!Directory.Exists(builtinRoot) || !File.Exists(manifestFile))
            {
                throw new Exception("HotUpdateRes 内置清单没有复制到：" + manifestFile);
            }

            Debug.Log($"[M3.3] HotUpdateRes 产物：{result.OutputPackageDirectory}");
            Debug.Log($"[M3.3] 内置文件：{manifestFile}");
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
