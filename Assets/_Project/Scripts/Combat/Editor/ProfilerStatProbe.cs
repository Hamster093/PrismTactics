/****************************************************
    文件：ProfilerStatProbe.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 16:40:00
    功能：编辑器工具：枚举当前可用的 ProfilerRecorder 指标，帮助性能基线选对统计项。
*****************************************************/

using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    /// <summary>
    /// 不同 Unity 版本/平台上可用的 Profiler 统计名不完全一致。
    /// 这个工具把所有包含关键词的指标打到 Console，基线页面据此选择正确名字和单位。
    /// </summary>
    public static class ProfilerStatProbe
    {
        private static readonly string[] Keywords =
        {
            "GC",
            "Alloc",
            "Draw",
            "SetPass",
            "Batch",
            "Canvas",
            "Main Thread",
            "PlayerLoop",
            "Render"
        };

        [MenuItem("Tools/PrismTactics/9 其他/9.2 性能/9.2.5 性能指标探测")]
        public static void DumpFromMenu()
        {
            Dump();
        }

        public static void DumpFromCli()
        {
            Dump();
        }

        private static void Dump()
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            Debug.Log($"[ProfilerStat] 共 {handles.Count} 个可用指标。");

            for (int i = 0; i < handles.Count; i++)
            {
                ProfilerRecorderDescription description =
                    ProfilerRecorderHandle.GetDescription(handles[i]);
                string name = description.Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                bool matched = false;
                for (int k = 0; k < Keywords.Length; k++)
                {
                    if (name.IndexOf(Keywords[k], System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matched = true;
                        break;
                    }
                }

                if (matched)
                {
                    Debug.Log($"[ProfilerStat] cat={description.Category} | name={name} | " +
                              $"unit={description.UnitType} | data={description.DataType}");
                }
            }
        }
    }
}
