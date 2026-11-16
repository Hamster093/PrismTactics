/****************************************************
    文件：BattlePerformanceProbe.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 15:25:00
    功能：60 敌人性能基准采集器：逐帧记录帧时间、GC、渲染与 UI 重建，输出峰值窗口报告。
*****************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PrismTactics.Combat;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 性能基准采集器。挂在基准场景的独立对象上，由 <see cref="PerformanceBenchmarkRunner"/> 启停。
    /// 所有指标按帧采样，最终只保留“峰值窗口”和“全程”两组汇总，避免把几千帧原始数据写进仓库。
    /// </summary>
    public sealed class BattlePerformanceProbe : MonoBehaviour
    {
        private sealed class MetricDefinition
        {
            public string Key;
            public string DisplayName;
            public string[] Aliases;
            public ProfilerMarkerDataUnit UnitType;
            public bool RawBytes;
            public ProfilerRecorder Recorder;
            public string SourceName;
            public bool Available;
            public string Unit;
        }

        private struct FrameSample
        {
            public double Time;
            public double DeltaMs;
            public int LiveEnemies;
            public double MainThreadMs;
            public double GcAllocBytes;
            public double DrawCalls;
            public double SetPassCalls;
            public double Batches;
            public double CanvasMs;
            public double CanvasBuildMs;
            public long ResourcesLoads;
        }

        [Serializable]
        public sealed class PerfMetricSummary
        {
            public string key;
            public string displayName;
            public string sourceName;
            public string unit;
            public bool available;
            public double avg;
            public double p95;
            public double max;
        }

        [Serializable]
        public sealed class PerfWindow
        {
            public string name;
            public int frameCount;
            public double seconds;
            public double avgFps;
            public int peakLiveEnemies;
            public double resourcesLoadsPerFrame;
            public long resourcesLoadsTotal;
            public List<PerfMetricSummary> metrics = new List<PerfMetricSummary>();
        }

        [Serializable]
        public sealed class PerfReport
        {
            public string label;
            public string sceneName;
            public string unityVersion;
            public string platform;
            public string startedAt;
            public int totalEnemies;
            public int peakLiveEnemies;
            public int sampledFrames;
            public PerfWindow overall;
            public PerfWindow peakWindow;
            public List<string> notes = new List<string>();
        }

        private static readonly string[] TimeAliases =
        {
            "PlayerLoop",
            "Main Thread",
            "CPU Total Frame Time",
            "CPU Main Thread Frame Time"
        };

        private static readonly string[] GcAliases =
        {
            "GC.Alloc",
            "GC Allocated In Frame",
            "GC Allocated"
        };

        private static readonly string[] DrawCallAliases =
        {
            "Draw Calls Count",
            "Draw Calls"
        };

        private static readonly string[] SetPassAliases =
        {
            "SetPass Calls Count",
            "SetPass Calls"
        };

        private static readonly string[] BatchAliases =
        {
            "Batches Count",
            "Batches"
        };

        private static readonly string[] CanvasAliases =
        {
            "UGUI.Rendering.UpdateBatches",
            "Canvas.SendWillRenderCanvases",
            "Canvas.SendWillRenderCanvases.Invoke"
        };

        private static readonly string[] CanvasBuildAliases =
        {
            "Canvas.BuildBatch",
            "Canvas.BuildBatch.Invoke"
        };

        private readonly List<FrameSample> _samples = new List<FrameSample>(4096);
        private readonly List<MetricDefinition> _metrics = new List<MetricDefinition>();

        private BattleController _controller;
        private bool _recording;
        private double _startRealtime;
        private double _durationSeconds;
        private long _lastResourcesLoads;
        private string _label = "baseline";

        public bool IsRecording => _recording;
        public PerfReport LastReport { get; private set; }
        public string LastJsonPath { get; private set; }
        public string LastMarkdownPath { get; private set; }

        /// <summary>
        /// 开始采样。durationSeconds 只限制采样时长，不改变游戏时间倍率。
        /// </summary>
        public void Begin(BattleController controller, string label, float durationSeconds)
        {
            _controller = controller;
            _label = string.IsNullOrEmpty(label) ? "baseline" : label;
            _durationSeconds = Mathf.Max(1f, durationSeconds);
            _samples.Clear();
            BuildRecorders();
            ResourcesLoadTracker.ResetCounter();
            UnitStatusBarFactory.ResetStats();
            ProjectileManager.ResetStats();
            _lastResourcesLoads = ResourcesLoadTracker.TotalCount;
            _startRealtime = Time.realtimeSinceStartupAsDouble;
            _recording = true;
            Debug.Log($"[Perf] 开始采样：{_label}，时长 {_durationSeconds:0.0}s，场景 {gameObject.scene.name}");
        }

        private void Update()
        {
            if (!_recording)
            {
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - _startRealtime;
            long loads = ResourcesLoadTracker.TotalCount;

            var sample = new FrameSample
            {
                Time = elapsed,
                DeltaMs = _samples.Count > 0 ? (elapsed - _samples[_samples.Count - 1].Time) * 1000.0 : 0.0,
                LiveEnemies = _controller != null ? _controller.LiveEnemies : 0,
                MainThreadMs = ReadMetric("mainThread"),
                GcAllocBytes = ReadMetric("gcAlloc"),
                DrawCalls = ReadMetric("drawCalls"),
                SetPassCalls = ReadMetric("setPass"),
                Batches = ReadMetric("batches"),
                CanvasMs = ReadMetric("canvas"),
                CanvasBuildMs = ReadMetric("canvasBuild"),
                ResourcesLoads = loads - _lastResourcesLoads
            };

            _lastResourcesLoads = loads;
            _samples.Add(sample);

            if (elapsed >= _durationSeconds)
            {
                Finish();
            }
        }

        /// <summary>
        /// 结束采样、生成报告并写盘；重复调用会直接返回上一次结果。
        /// </summary>
        public PerfReport Finish()
        {
            if (!_recording && LastReport != null)
            {
                return LastReport;
            }

            _recording = false;
            LastReport = BuildReport();
            WriteReport(LastReport);
            DisposeRecorders();
            Debug.Log($"[Perf] 采样结束：峰值同屏 {LastReport.peakLiveEnemies} 个敌人；" +
                      $"峰值窗口平均 {LastReport.peakWindow.avgFps:0.0} FPS；报告 {LastJsonPath}");
            return LastReport;
        }

        private void OnDestroy()
        {
            DisposeRecorders();
        }

        private double ReadMetric(string key)
        {
            for (int i = 0; i < _metrics.Count; i++)
            {
                MetricDefinition metric = _metrics[i];
                if (metric.Key != key || !metric.Available || !metric.Recorder.Valid)
                {
                    continue;
                }

                double value = metric.Recorder.CurrentValueAsDouble;
                // GC.Alloc 的元数据单位被标成时间，但样本值实际是字节，不能做 ns→ms 转换。
                if (metric.RawBytes)
                {
                    return value;
                }

                if (metric.UnitType == ProfilerMarkerDataUnit.TimeNanoseconds)
                {
                    return value / 1000000.0; // 纳秒转毫秒，报告统一用 ms
                }

                return value;
            }

            return 0.0;
        }

        private void BuildRecorders()
        {
            DisposeRecorders();
            _metrics.Clear();

            AddMetric("mainThread", "主线程帧时间", TimeAliases, "ms");
            AddMetric("gcAlloc", "GC 分配", GcAliases, "B", true);
            AddMetric("drawCalls", "Draw Calls", DrawCallAliases, "次");
            AddMetric("setPass", "SetPass Calls", SetPassAliases, "次");
            AddMetric("batches", "Batches", BatchAliases, "次");
            AddMetric("canvas", "Canvas 提交", CanvasAliases, "ms");
            AddMetric("canvasBuild", "Canvas 合批", CanvasBuildAliases, "ms");
        }

        private void AddMetric(string key, string displayName, string[] aliases, string unit, bool rawBytes = false)
        {
            var metric = new MetricDefinition
            {
                Key = key,
                DisplayName = displayName,
                Aliases = aliases,
                Unit = unit,
                RawBytes = rawBytes,
                Available = false,
                SourceName = string.Empty
            };

            if (TryCreateRecorder(aliases, out ProfilerRecorder recorder,
                    out string sourceName, out ProfilerMarkerDataUnit unitType))
            {
                metric.Recorder = recorder;
                metric.SourceName = sourceName;
                metric.UnitType = unitType;
                metric.Available = true;
                metric.Unit = rawBytes ? "B" : ToUnitLabel(unitType, unit);
            }

            _metrics.Add(metric);
        }

        private static bool TryCreateRecorder(
            string[] aliases,
            out ProfilerRecorder recorder,
            out string sourceName,
            out ProfilerMarkerDataUnit unitType)
        {
            recorder = default;
            sourceName = string.Empty;
            unitType = ProfilerMarkerDataUnit.Undefined;

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            for (int a = 0; a < aliases.Length; a++)
            {
                string alias = aliases[a];

                // 先精确匹配，找不到再做忽略大小写的包含匹配，避免误选同前缀的长名字。
                for (int i = 0; i < handles.Count; i++)
                {
                    ProfilerRecorderDescription description =
                        ProfilerRecorderHandle.GetDescription(handles[i]);
                    if (!string.Equals(description.Name, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    recorder = ProfilerRecorder.StartNew(description.Category, description.Name);
                    if (recorder.Valid)
                    {
                        sourceName = description.Name;
                        unitType = description.UnitType;
                        return true;
                    }
                }

                for (int i = 0; i < handles.Count; i++)
                {
                    ProfilerRecorderDescription description =
                        ProfilerRecorderHandle.GetDescription(handles[i]);
                    if (description.Name == null
                        || description.Name.IndexOf(alias, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    recorder = ProfilerRecorder.StartNew(description.Category, description.Name);
                    if (recorder.Valid)
                    {
                        sourceName = description.Name;
                        unitType = description.UnitType;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string ToUnitLabel(ProfilerMarkerDataUnit unitType, string fallback)
        {
            switch (unitType)
            {
                case ProfilerMarkerDataUnit.TimeNanoseconds:
                    return "ms";
                case ProfilerMarkerDataUnit.Bytes:
                    return "B";
                case ProfilerMarkerDataUnit.Count:
                    return "次";
                default:
                    return fallback;
            }
        }

        private void DisposeRecorders()
        {
            for (int i = 0; i < _metrics.Count; i++)
            {
                if (_metrics[i].Recorder.Valid)
                {
                    _metrics[i].Recorder.Dispose();
                }
            }
        }

        private PerfReport BuildReport()
        {
            var report = new PerfReport
            {
                label = _label,
                sceneName = gameObject.scene.name,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                totalEnemies = _controller != null ? _controller.TotalEnemies : 0,
                sampledFrames = _samples.Count
            };

            int peak = 0;
            for (int i = 0; i < _samples.Count; i++)
            {
                peak = Mathf.Max(peak, _samples[i].LiveEnemies);
            }

            report.peakLiveEnemies = peak;

            int threshold = Mathf.Max(1, Mathf.CeilToInt(peak * 0.8f));
            var overall = new List<FrameSample>(_samples);
            var peakFrames = new List<FrameSample>();
            for (int i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].LiveEnemies >= threshold)
                {
                    peakFrames.Add(_samples[i]);
                }
            }

            report.overall = Summarize("全程", overall);
            report.peakWindow = Summarize("峰值窗口（同屏 ≥ 峰值 80%）", peakFrames);

            for (int i = 0; i < _metrics.Count; i++)
            {
                if (!_metrics[i].Available)
                {
                    report.notes.Add($"指标不可用：{_metrics[i].DisplayName}（{string.Join("/", _metrics[i].Aliases)}）");
                }
            }

            report.notes.Add("峰值窗口用于排除出怪前的空场帧；阈值 = 峰值同屏数 × 80%。");
            report.notes.Add("Draw Calls / SetPass / Batches 需要启用渲染设备；batchmode 下若不可用，报告会标记为 0。");
            report.notes.Add(
                $"血条池：本局创建 {UnitStatusBarFactory.CreatedCount} 套，回收后池内 {UnitStatusBarFactory.PooledCount} 套。");
            report.notes.Add(
                $"弹道池：本局创建 {ProjectileManager.CreatedCount} 发，回收后池内 {ProjectileManager.PooledCount} 发。");
            return report;
        }

        private PerfWindow Summarize(string name, List<FrameSample> frames)
        {
            var window = new PerfWindow
            {
                name = name,
                frameCount = frames.Count
            };

            if (frames.Count == 0)
            {
                AddEmptyMetrics(window);
                return window;
            }

            window.seconds = frames[frames.Count - 1].Time - frames[0].Time;
            if (window.seconds <= 0.0001)
            {
                window.seconds = frames.Count / 60.0;
            }

            window.avgFps = frames.Count / window.seconds;
            window.peakLiveEnemies = 0;
            long loads = 0;

            for (int i = 0; i < frames.Count; i++)
            {
                window.peakLiveEnemies = Mathf.Max(window.peakLiveEnemies, frames[i].LiveEnemies);
                loads += frames[i].ResourcesLoads;
            }

            window.resourcesLoadsTotal = loads;
            window.resourcesLoadsPerFrame = loads / (double)frames.Count;

            AddMetricSummary(window, "frameMsMeasured", "实测帧间隔", "ms",
                Select(frames, f => f.DeltaMs));
            AddMetricSummary(window, "mainThread", "主线程帧时间", "ms",
                Select(frames, f => f.MainThreadMs));
            AddMetricSummary(window, "gcAlloc", "GC 分配", "B",
                Select(frames, f => f.GcAllocBytes));
            AddMetricSummary(window, "drawCalls", "Draw Calls", "次",
                Select(frames, f => f.DrawCalls));
            AddMetricSummary(window, "setPass", "SetPass Calls", "次",
                Select(frames, f => f.SetPassCalls));
            AddMetricSummary(window, "batches", "Batches", "次",
                Select(frames, f => f.Batches));
            AddMetricSummary(window, "canvas", "Canvas 提交", "ms",
                Select(frames, f => f.CanvasMs));
            AddMetricSummary(window, "canvasBuild", "Canvas 合批", "ms",
                Select(frames, f => f.CanvasBuildMs));
            AddMetricSummary(window, "resourcesLoads", "Resources.Load / 帧", "次",
                Select(frames, f => f.ResourcesLoads));

            return window;
        }

        private void AddEmptyMetrics(PerfWindow window)
        {
            AddMetricSummary(window, "frameMsMeasured", "实测帧间隔", "ms", new List<double>());
            AddMetricSummary(window, "mainThread", "主线程帧时间", "ms", new List<double>());
            AddMetricSummary(window, "gcAlloc", "GC 分配", "B", new List<double>());
            AddMetricSummary(window, "drawCalls", "Draw Calls", "次", new List<double>());
            AddMetricSummary(window, "setPass", "SetPass Calls", "次", new List<double>());
            AddMetricSummary(window, "batches", "Batches", "次", new List<double>());
            AddMetricSummary(window, "canvas", "Canvas 提交", "ms", new List<double>());
            AddMetricSummary(window, "canvasBuild", "Canvas 合批", "ms", new List<double>());
            AddMetricSummary(window, "resourcesLoads", "Resources.Load / 帧", "次", new List<double>());
        }

        private void AddMetricSummary(
            PerfWindow window,
            string key,
            string displayName,
            string unit,
            List<double> values)
        {
            var summary = new PerfMetricSummary
            {
                key = key,
                displayName = displayName,
                unit = unit,
                available = values.Count > 0,
                sourceName = FindSourceName(key)
            };

            if (values.Count > 0)
            {
                double sum = 0.0;
                double max = double.MinValue;
                for (int i = 0; i < values.Count; i++)
                {
                    sum += values[i];
                    max = Math.Max(max, values[i]);
                }

                var sorted = new List<double>(values);
                sorted.Sort();
                int p95Index = Mathf.Clamp(
                    Mathf.CeilToInt(sorted.Count * 0.95f) - 1,
                    0,
                    sorted.Count - 1);

                summary.avg = sum / values.Count;
                summary.p95 = sorted[p95Index];
                summary.max = max;
            }

            window.metrics.Add(summary);
        }

        private string FindSourceName(string key)
        {
            if (key == "frameMsMeasured")
            {
                return "采样时间戳差值";
            }

            if (key == "resourcesLoads")
            {
                return "ResourcesLoadTracker";
            }

            if (key == "gcAlloc")
            {
                return "GC.Alloc（字节）";
            }

            for (int i = 0; i < _metrics.Count; i++)
            {
                if (_metrics[i].Key == key)
                {
                    return _metrics[i].SourceName;
                }
            }

            return string.Empty;
        }

        private static List<double> Select(List<FrameSample> frames, Func<FrameSample, double> selector)
        {
            var values = new List<double>(frames.Count);
            for (int i = 0; i < frames.Count; i++)
            {
                values.Add(selector(frames[i]));
            }

            return values;
        }

        private void WriteReport(PerfReport report)
        {
            string directory = Path.Combine(Application.dataPath, "..", "Logs", "Perf");
            Directory.CreateDirectory(directory);

            string safeLabel = Sanitize(report.label);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = $"perf_{safeLabel}_{stamp}";

            LastJsonPath = Path.Combine(directory, baseName + ".json");
            LastMarkdownPath = Path.Combine(directory, baseName + ".md");

            File.WriteAllText(LastJsonPath, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(LastMarkdownPath, BuildMarkdown(report), new UTF8Encoding(false));

            // 额外保留一份 latest，方便文档和截图引用固定路径。
            File.WriteAllText(
                Path.Combine(directory, $"perf_{safeLabel}_latest.json"),
                JsonUtility.ToJson(report, true),
                new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(directory, $"perf_{safeLabel}_latest.md"),
                BuildMarkdown(report),
                new UTF8Encoding(false));
        }

        private static string BuildMarkdown(PerfReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# PrismTactics 60 敌人性能采样");
            builder.AppendLine();
            builder.AppendLine($"- 标签：`{report.label}`");
            builder.AppendLine($"- 场景：`{report.sceneName}`");
            builder.AppendLine($"- Unity：`{report.unityVersion}`");
            builder.AppendLine($"- 平台：`{report.platform}`");
            builder.AppendLine($"- 采样时间：{report.startedAt}");
            builder.AppendLine($"- 总敌人数：{report.totalEnemies}");
            builder.AppendLine($"- 峰值同屏敌人：**{report.peakLiveEnemies}**");
            builder.AppendLine($"- 采样帧数：{report.sampledFrames}");
            builder.AppendLine();

            AppendWindow(builder, report.peakWindow);
            AppendWindow(builder, report.overall);

            builder.AppendLine("## 说明");
            for (int i = 0; i < report.notes.Count; i++)
            {
                builder.AppendLine($"- {report.notes[i]}");
            }

            return builder.ToString();
        }

        private static void AppendWindow(StringBuilder builder, PerfWindow window)
        {
            if (window == null)
            {
                return;
            }

            builder.AppendLine($"## {window.name}");
            builder.AppendLine();
            builder.AppendLine(
                $"帧数 {window.frameCount}，时长 {window.seconds:0.00}s，" +
                $"平均 {window.avgFps:0.0} FPS，窗口内峰值同屏 {window.peakLiveEnemies}，" +
                $"Resources.Load {window.resourcesLoadsPerFrame:0.00} 次/帧。");
            builder.AppendLine();
            builder.AppendLine("| 指标 | 平均 | P95 | 最大 | 单位 | 数据源 |");
            builder.AppendLine("|---|---|---|---|---|---|");

            for (int i = 0; i < window.metrics.Count; i++)
            {
                PerfMetricSummary metric = window.metrics[i];
                string source = metric.available ? metric.sourceName : "不可用";
                builder.AppendLine(
                    $"| {metric.displayName} | {metric.avg:0.000} | {metric.p95:0.000} | " +
                    $"{metric.max:0.000} | {metric.unit} | {source} |");
            }

            builder.AppendLine();
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "run";
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            }

            return builder.ToString();
        }
    }
}
