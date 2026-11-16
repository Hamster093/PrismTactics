/****************************************************
    文件：Phase3ReleaseBuilder.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 16:20:00
    功能：M3.2 发布清单：构建指定内容版本、写 release.json、同步到 StreamingAssets 并校验。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PrismTactics.Battle;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// release.json 是唯一发布开关：BattleRes 与 HotUpdateRes 的版本在这里一起钉死。
    /// 发布顺序：先构建两个包，最后写清单指针。
    /// </summary>
    public static class Phase3ReleaseBuilder
    {
        private const string SourceDirectory = "Assets/GameContent/Release";
        private const string StreamingDirectory = "Assets/StreamingAssets/Release";

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.2 内容/2.2.2 发布内容版本", false, 28)]
        public static void PublishFromMenu()
        {
            Publish();
            EditorUtility.DisplayDialog("M3.2", "发布完成，release.json 已更新。", "好");
        }

        [MenuItem("Tools/PrismTactics/2 构建与热更/2.2 内容/2.2.3 校验发布清单", false, 29)]
        public static void ValidateFromMenu()
        {
            Validate();
        }

        /// <summary>命令行入口：PRISM_RELEASE_VERSION / PRISM_RELEASE_PREVIOUS 指定版本。</summary>
        public static void PublishFromCli()
        {
            int exitCode = 0;
            try
            {
                Publish();
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.2] 发布失败：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>命令行入口：只校验清单与内置包是否一致。</summary>
        public static void ValidateFromCli()
        {
            int exitCode = 0;
            try
            {
                Validate();
            }
            catch (Exception exception)
            {
                Debug.LogError("[M3.2] 校验失败：" + exception);
                exitCode = 1;
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>
        /// 构建内容包 → 更新 release.json → 复制到 StreamingAssets。
        /// 环境变量：PRISM_RELEASE_VERSION、PRISM_RELEASE_PREVIOUS、PRISM_RELEASE_APP、
        /// PRISM_RELEASE_MIN_APP、PRISM_HOTUPDATE_VERSION、PRISM_CONTENT_KEEP_BUILTIN。
        /// </summary>
        public static void Publish()
        {
            string version = Environment.GetEnvironmentVariable("PRISM_RELEASE_VERSION");
            if (string.IsNullOrWhiteSpace(version))
            {
                version = Phase3ContentBuilder.GetTargetVersion();
            }
            version = version.Trim();

            bool keepBuiltin = Environment.GetEnvironmentVariable("PRISM_CONTENT_KEEP_BUILTIN") != "0";
            YooAsset.Editor.BuildResult buildResult = Phase3ContentBuilder.BuildContent(version, keepBuiltin);
            if (!buildResult.Success)
            {
                throw new Exception("BattleRes 构建失败：" + buildResult.ErrorInfo);
            }

            ContentReleaseCatalog catalog = LoadOrCreateCatalog();
            string oldLatest = catalog.latest;
            string previous = Environment.GetEnvironmentVariable("PRISM_RELEASE_PREVIOUS");
            if (string.IsNullOrWhiteSpace(previous))
            {
                previous = !string.IsNullOrEmpty(oldLatest)
                           && !string.Equals(oldLatest, version, StringComparison.Ordinal)
                    ? oldLatest
                    : catalog.previous;
            }

            string hotUpdateVersion = Environment.GetEnvironmentVariable("PRISM_HOTUPDATE_VERSION");
            if (string.IsNullOrWhiteSpace(hotUpdateVersion))
            {
                hotUpdateVersion = version;
            }

            ContentReleaseEntry entry = catalog.Find(version);
            if (entry == null)
            {
                entry = new ContentReleaseEntry { releaseId = version };
                catalog.releases.Add(entry);
            }

            entry.battleResVersion = version;
            entry.hotUpdateResVersion = hotUpdateVersion.Trim();
            entry.note = Environment.GetEnvironmentVariable("PRISM_RELEASE_NOTE") ?? entry.note;

            catalog.schemaVersion = ContentReleaseStore.ConfigSchemaVersion;
            catalog.appVersion = Environment.GetEnvironmentVariable("PRISM_RELEASE_APP")
                                 ?? (string.IsNullOrEmpty(catalog.appVersion) ? "0.1.0" : catalog.appVersion);
            catalog.minAppVersion = Environment.GetEnvironmentVariable("PRISM_RELEASE_MIN_APP")
                                    ?? (string.IsNullOrEmpty(catalog.minAppVersion) ? "0.1" : catalog.minAppVersion);
            catalog.latest = version;
            catalog.previous = previous ?? string.Empty;

            WriteCatalog(catalog);
            SyncReleaseToStreaming();
            Debug.Log($"[M3.2] 发布完成：latest={catalog.latest}，previous={catalog.previous}，" +
                      $"{version}（BattleRes + HotUpdateRes {entry.hotUpdateResVersion}）");
            Validate();
        }

        /// <summary>Player 构建前调用：确保 StreamingAssets 里是最新清单。</summary>
        public static void SyncReleaseToStreaming()
        {
            string source = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, SourceDirectory, ContentReleaseStore.ReleaseFileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("发布清单不存在，先执行 M3.2 发布：" + source);
            }

            string destinationDirectory = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, StreamingDirectory);
            Directory.CreateDirectory(destinationDirectory);
            string destination = Path.Combine(destinationDirectory, ContentReleaseStore.ReleaseFileName);
            File.Copy(source, destination, true);
            AssetDatabase.Refresh();
            Debug.Log("[M3.2] release.json 已同步到：" + destination);
        }

        /// <summary>校验 latest/previous 存在、schema 一致、每个版本的 BattleRes 清单都在内置目录。</summary>
        public static void Validate()
        {
            ContentReleaseCatalog catalog = ContentReleaseStore.Load(out string error);
            if (catalog == null)
            {
                throw new Exception(error);
            }

            if (string.IsNullOrEmpty(catalog.latest))
            {
                throw new Exception("release.json 没有 latest");
            }

            ContentReleaseEntry latest = catalog.Find(catalog.latest);
            if (latest == null)
            {
                throw new Exception("latest 指向不存在的发布：" + catalog.latest);
            }

            if (!string.IsNullOrEmpty(catalog.previous) && catalog.Find(catalog.previous) == null)
            {
                throw new Exception("previous 指向不存在的发布：" + catalog.previous);
            }

            string builtinRoot = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Assets/StreamingAssets/yoo", Phase3ContentBuilder.PackageName);
            for (int i = 0; i < catalog.releases.Count; i++)
            {
                ContentReleaseEntry entry = catalog.releases[i];
                if (entry == null || string.IsNullOrEmpty(entry.battleResVersion))
                {
                    throw new Exception("存在缺少 battleResVersion 的发布记录");
                }

                string manifest = Path.Combine(builtinRoot,
                    $"{Phase3ContentBuilder.PackageName}_{entry.battleResVersion}.bytes");
                if (!File.Exists(manifest))
                {
                    throw new Exception($"内置目录缺少 {entry.releaseId} 的清单：{manifest}");
                }
            }

            Debug.Log($"[M3.2] 发布清单校验通过：{catalog.releases.Count} 个版本，" +
                      $"latest={catalog.latest}，previous={catalog.previous}，schema={catalog.schemaVersion}");
        }

        private static ContentReleaseCatalog LoadOrCreateCatalog()
        {
            ContentReleaseCatalog catalog = ContentReleaseStore.Load(out _);
            return catalog ?? new ContentReleaseCatalog
            {
                schemaVersion = ContentReleaseStore.ConfigSchemaVersion,
                releases = new List<ContentReleaseEntry>(),
            };
        }

        private static void WriteCatalog(ContentReleaseCatalog catalog)
        {
            string directory = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, SourceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, ContentReleaseStore.ReleaseFileName);
            File.WriteAllText(path, JsonUtility.ToJson(catalog, true), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("[M3.2] release.json 写入：" + path);
        }
    }
}
