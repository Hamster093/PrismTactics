/****************************************************
    文件：ContentReleaseStore.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 16:08:00
    功能：读取 release.json、选择发布版本（显式参数 or latest/previous），并做 schema/兼容性校验。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 运行时只认这张清单：不允许单独指定 BattleRes 或 HotUpdateRes 的版本。
    /// </summary>
    public static class ContentReleaseStore
    {
        /// <summary>配置结构版本；代码升级配置结构时同步 +1。</summary>
        public const int ConfigSchemaVersion = 1;

        public const string ReleaseFileName = "release.json";

        /// <summary>编辑器读 GameContent 源文件，Player 读随包 StreamingAssets。</summary>
        public static string ReleasePath
        {
            get
            {
#if UNITY_EDITOR
                return Path.Combine(Application.dataPath, "GameContent/Release", ReleaseFileName);
#else
                return Path.Combine(Application.streamingAssetsPath, "Release", ReleaseFileName);
#endif
            }
        }

        /// <summary>
        /// 读取并校验清单；失败返回 null 并给出原因，调用方必须显式报错。
        /// </summary>
        public static ContentReleaseCatalog Load(out string error)
        {
            error = null;
            if (!File.Exists(ReleasePath))
            {
                error = "发布清单不存在：" + ReleasePath;
                return null;
            }

            ContentReleaseCatalog catalog;
            try
            {
                catalog = JsonUtility.FromJson<ContentReleaseCatalog>(File.ReadAllText(ReleasePath));
            }
            catch (Exception exception)
            {
                error = "发布清单解析失败：" + exception.Message;
                return null;
            }

            if (catalog == null)
            {
                error = "发布清单为空：" + ReleasePath;
                return null;
            }

            if (catalog.schemaVersion != ConfigSchemaVersion)
            {
                error = $"配置结构版本不兼容：清单 {catalog.schemaVersion}，代码 {ConfigSchemaVersion}";
                return null;
            }

            if (catalog.releases == null || catalog.releases.Count == 0)
            {
                error = "发布清单没有任何 releases";
                return null;
            }

            return catalog;
        }

        /// <summary>命令行 --content-release / --content-version 优先，用于手动回退。</summary>
        public static string GetRequestedReleaseId()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--content-release", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(args[i], "--content-version", StringComparison.OrdinalIgnoreCase))
                {
                    string value = args[i + 1];
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// 显式版本只试一次；默认 latest → previous，用于自动回退。
        /// </summary>
        public static List<string> GetCandidateReleaseIds(ContentReleaseCatalog catalog, out bool explicitRequest)
        {
            string requested = GetRequestedReleaseId();
            var result = new List<string>();
            if (!string.IsNullOrEmpty(requested))
            {
                explicitRequest = true;
                result.Add(requested);
                return result;
            }

            explicitRequest = false;
            if (!string.IsNullOrEmpty(catalog.latest))
            {
                result.Add(catalog.latest);
            }

            if (!string.IsNullOrEmpty(catalog.previous)
                && !string.Equals(catalog.previous, catalog.latest, StringComparison.Ordinal))
            {
                result.Add(catalog.previous);
            }

            return result;
        }

        /// <summary>minAppVersion 用于阻止旧 Player 加载新内容；为空表示不限制。</summary>
        public static bool IsAppVersionCompatible(ContentReleaseCatalog catalog, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(catalog.minAppVersion))
            {
                return true;
            }

            if (!Version.TryParse(catalog.minAppVersion, out Version minVersion)
                || !Version.TryParse(Application.version, out Version appVersion))
            {
                Debug.LogWarning($"[Content] 版本号无法解析，跳过兼容检查：min={catalog.minAppVersion} app={Application.version}");
                return true;
            }

            if (appVersion < minVersion)
            {
                error = $"当前 App {appVersion} 低于内容要求 {minVersion}";
                return false;
            }

            return true;
        }
    }
}
