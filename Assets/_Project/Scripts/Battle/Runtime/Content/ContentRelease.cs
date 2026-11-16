/****************************************************
    文件：ContentRelease.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/11/1 16:05:00
    功能：发布清单数据结构：把 BattleRes 与 HotUpdateRes 的版本钉在同一次发布里。
*****************************************************/

using System;
using System.Collections.Generic;

namespace PrismTactics.Battle
{
    /// <summary>一次内容发布：两个包的版本必须一起切换。</summary>
    [Serializable]
    public sealed class ContentReleaseEntry
    {
        public string releaseId;
        public string battleResVersion;
        public string hotUpdateResVersion;
        public string hotUpdateDllHash;
        public string note;
    }

    /// <summary>
    /// release.json 的运行时结构。
    /// latest/previous 是清单指针；releases 是实际可用的发布记录。
    /// </summary>
    [Serializable]
    public sealed class ContentReleaseCatalog
    {
        public string appVersion;
        public string minAppVersion;
        public int schemaVersion;
        public string latest;
        public string previous;
        public List<ContentReleaseEntry> releases = new List<ContentReleaseEntry>();

        public ContentReleaseEntry Find(string releaseId)
        {
            if (string.IsNullOrEmpty(releaseId) || releases == null)
            {
                return null;
            }

            for (int i = 0; i < releases.Count; i++)
            {
                ContentReleaseEntry entry = releases[i];
                if (entry != null && string.Equals(entry.releaseId, releaseId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
