/****************************************************
    文件：PerformanceBenchmarkRunner.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/30 15:40:00
    功能：基准场景驱动器：固定部署、自动开技能、启动采集并在结束后写报告。
*****************************************************/

using System;
using System.Collections;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 挂在 M1_Perf60 场景里，负责把“真实战斗”跑成可重复的压测：
    /// 固定格子部署 4 个干员、固定选中一个、技能就绪自动释放、固定采样时长。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class PerformanceBenchmarkRunner : MonoBehaviour
    {
        [Header("压测参数")]
        [SerializeField] private string _label = "baseline";
        [SerializeField] private float _sampleSeconds = 20f;
        [SerializeField] private bool _autoRun = true;
        [SerializeField] private bool _autoActivateSkills = true;
        [Tooltip("勾选后恢复旧版每帧轮询 HUD，仅用于 Profiler 前后对照截图。")]
        [InspectorName("旧版 HUD 轮询（对照用）")]
        [SerializeField] private bool _legacyHudPolling;
        [Tooltip("关闭后不绘制左上角基准浮层，用于排除浮层自身产生的 GC。")]
        [InspectorName("显示基准浮层")]
        [SerializeField] private bool _showOverlay = true;
        [Tooltip("打包 Player 自动跑基准时，结束后自动退出。命令行 -perfQuit 也会打开。")]
        [InspectorName("跑完自动退出（Player）")]
        [SerializeField] private bool _quitWhenFinished;
        [SerializeField] private int _targetFrameRate = 60;
        [SerializeField] private int _selectedOperatorIndex;

        [Header("固定部署格子（按 operators.json 顺序）")]
        [SerializeField]
        private Vector2Int[] _deployCells =
        {
            new Vector2Int(5, 3),
            new Vector2Int(7, 1),
            new Vector2Int(3, 3),
            new Vector2Int(4, 1)
        };

        [Tooltip("部署干员数量上限；0 表示全部。压测固定 4 个 AOT 干员，避开热更技能带来的波动。")]
        [SerializeField] private int _operatorLimit;

        [Tooltip("伤害数字压测：只部署高速攻击干员，对高血量木桩持续输出。")]
        [SerializeField] private bool _damageNumberStress;

        [Tooltip("伤害数字压测的速射干员数量，每个 15 次/秒。")]
        [SerializeField] private int _stressOperatorCount = 6;

        private BattlePerformanceProbe _probe;
        private BattleController _controller;
        private readonly List<string> _deployed = new List<string>();
        private float _startRealtime;

        public bool Finished { get; private set; }
        public bool Running { get; private set; }
        public BattlePerformanceProbe.PerfReport Result { get; private set; }
        public string ReportJsonPath => _probe != null ? _probe.LastJsonPath : string.Empty;
        public string ReportMarkdownPath => _probe != null ? _probe.LastMarkdownPath : string.Empty;
        public IReadOnlyList<string> DeployedEntries => _deployed;

        private void Awake()
        {
            // 环境变量用于同一次构建里区分优化前后，不需要改场景。
            string overrideLabel = Environment.GetEnvironmentVariable("PRISM_PERF_LABEL");
            if (!string.IsNullOrEmpty(overrideLabel))
            {
                _label = overrideLabel;
            }

            string legacyOverride = Environment.GetEnvironmentVariable("PRISM_PERF_LEGACY_HUD");
            if (!string.IsNullOrEmpty(legacyOverride) && legacyOverride == "1")
            {
                _legacyHudPolling = true;
            }

            string overlayOverride = Environment.GetEnvironmentVariable("PRISM_PERF_OVERLAY");
            if (!string.IsNullOrEmpty(overlayOverride) && overlayOverride == "0")
            {
                _showOverlay = false;
            }

            string[] commandLine = Environment.GetCommandLineArgs();
            for (int i = 0; i < commandLine.Length; i++)
            {
                if (commandLine[i] == "-perfQuit")
                {
                    _quitWhenFinished = true;
                }
            }

            // Player 窗口失焦时也要继续跑，否则基准会因为 runInBackground=false 停住。
            Application.runInBackground = true;

            Application.targetFrameRate = _targetFrameRate;
            QualitySettings.vSyncCount = 0;
            // 上一次测试/选关面板可能把 timeScale 留在 0，基准必须强制恢复 1 倍速。
            Time.timeScale = 1f;
            UnityEngine.Random.InitState(12345);
        }

        private void Start()
        {
            if (_autoRun)
            {
                StartCoroutine(RunBenchmark());
            }
        }

        private IEnumerator RunBenchmark()
        {
            Running = true;
            _startRealtime = Time.realtimeSinceStartup;
            Time.timeScale = 1f;

            // 基准只关心战斗，清掉跨局选择的技能/藏品，保证重复采样条件一致。
            RunSession.ResetForBenchmark();
            yield return null;

            float deadline = Time.realtimeSinceStartup + 15f;
            while (_controller == null && Time.realtimeSinceStartup < deadline)
            {
                _controller = FindObjectOfType<BattleController>();
                yield return null;
            }

            if (_controller == null)
            {
                Debug.LogError("[Perf] 基准场景没有找到 BattleController，请检查 Bootstrap 的 Auto Build 和 Map Id。");
                Finished = true;
                Running = false;
                yield break;
            }

            // 等 HUD 绑定完成，避免把初始化帧算进采样；最多等 5 秒，缺 HUD 不阻塞部署。
            float hudDeadline = Time.realtimeSinceStartup + 5f;
            while (UIManager.Instance == null && Time.realtimeSinceStartup < hudDeadline)
            {
                yield return null;
            }

            if (UIManager.Instance == null)
            {
                Debug.LogWarning("[Perf] UIManager 未就绪：场景可能缺 HUD/Canvas，压测继续但 HUD 相关指标会缺失。");
            }

            BattleHudBinder hud = FindObjectOfType<BattleHudBinder>();
            if (hud != null)
            {
                hud.SetLegacyPolling(_legacyHudPolling);
            }

            DeployOperators(_controller);
            yield return null;
            SelectOperator(_controller);
            yield return null;

            // 采样期间关闭基准浮层：OnGUI/GUI.Repaint 自身每帧会产生 ~1.4 KB GC，会污染测量
            if (_showOverlay)
            {
                _showOverlay = false;
                Debug.Log("[Perf] 采样期间关闭基准浮层，避免 IMGUI 的 GC 干扰。");
            }

            _probe = gameObject.AddComponent<BattlePerformanceProbe>();
            _probe.Begin(_controller, _label, _sampleSeconds);

            while (_probe != null && _probe.IsRecording)
            {
                if (_autoActivateSkills)
                {
                    ActivateReadySkills(_controller);
                }

                yield return null;
            }

            Result = _probe != null ? _probe.LastReport : null;
            Finished = true;
            Running = false;
            Debug.Log($"[Perf] 基准完成：标签 {_label}，峰值同屏 {Result?.peakLiveEnemies ?? 0}，" +
                      $"报告 {ReportJsonPath}");

            if (_quitWhenFinished)
            {
                // 留半秒把日志和报告刷盘，再退出 Player；编辑器里 Quit 不会真的退出。
                yield return new WaitForSecondsRealtime(0.5f);
                Application.Quit();
            }
        }

        private void DeployOperators(BattleController controller)
        {
            if (controller.State != BattleState.Running)
            {
                Debug.LogError("[Perf] 战斗已结束（" + controller.State + "），无法部署；检查场景是否需要重新生成。");
                return;
            }

            if (_damageNumberStress)
            {
                DeployStressOperator(controller);
                return;
            }

            if (controller.Operators == null || controller.Operators.Count == 0)
            {
                Debug.LogError("[Perf] operators.json 没有干员，无法执行基准。");
                return;
            }

            var usedCells = new HashSet<Vector2Int>();
            int count = _operatorLimit > 0
                ? Mathf.Min(_operatorLimit, controller.Operators.Count)
                : controller.Operators.Count;
            for (int i = 0; i < count; i++)
            {
                OperatorConfig config = controller.Operators[i];
                if (config == null)
                {
                    continue;
                }

                Vector2Int preferred = i < _deployCells.Length
                    ? _deployCells[i]
                    : FindFallbackCell(controller, config, usedCells);

                if (!TryDeploy(controller, config, preferred, usedCells))
                {
                    Vector2Int fallback = FindFallbackCell(controller, config, usedCells);
                    if (!TryDeploy(controller, config, fallback, usedCells))
                    {
                        Debug.LogWarning($"[Perf] 干员 {config.operatorId} 找不到可部署格子，跳过。");
                    }
                }
            }

            Debug.Log($"[Perf] 已部署 {_deployed.Count} 个干员：{string.Join("、", _deployed)}");
        }

        /// <summary>
        /// 伤害数字压测：克隆多个高速攻击干员，压在木桩身上持续产生伤害数字。
        /// 每个干员攻速 2 tick（30Hz 下 15 次/秒），6 个就是 90 次/秒。
        /// </summary>
        private void DeployStressOperator(BattleController controller)
        {
            if (controller.Operators == null || controller.Operators.Count == 0)
            {
                Debug.LogError("[Perf] 没有可用干员，无法执行伤害数字压测。");
                return;
            }

            OperatorConfig source = controller.Operators[0];
            var usedCells = new HashSet<Vector2Int>();
            int wanted = Mathf.Max(1, _stressOperatorCount);

            for (int i = 0; i < wanted; i++)
            {
                OperatorConfig fast = Instantiate(source);
                fast.name = source.name + "_Stress" + i;
                fast.operatorId = "stress_dps_" + i;
                fast.displayName = "压测速射" + i;
                fast.deployCost = 0;
                fast.maxHealth = 9999f;
                fast.attack = 1f;
                fast.attackIntervalTicks = 2;
                fast.windupTicks = 0;
                fast.blockCount = 0;
                fast.skills = new SkillConfig[0];
                fast.attackRange = 999f;
                fast.rangeCells = BuildSquareRange(12);

                if (!TryDeployStress(controller, fast, usedCells))
                {
                    Debug.LogWarning($"[Perf] 第 {i + 1} 个速射干员没有可用格子。");
                    break;
                }
            }

            Debug.Log($"[Perf] 伤害数字压测：部署 {_deployed.Count} 个速射干员，理论 {_deployed.Count * 15} 次/秒；{string.Join("、", _deployed)}");
        }

        /// <summary>把一个速射干员放到第一个空闲可部署格。</summary>
        private bool TryDeployStress(BattleController controller, OperatorConfig config, HashSet<Vector2Int> usedCells)
        {
            for (int y = 0; y < controller.Map.Height; y++)
            {
                for (int x = 0; x < controller.Map.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (usedCells.Contains(cell)
                        || !controller.CanDeployAt(config, cell)
                        || !controller.TryDeploy(config, cell, GridFacing.Right))
                    {
                        continue;
                    }

                    usedCells.Add(cell);
                    _deployed.Add($"{config.operatorId}@({x},{y})");
                    return true;
                }
            }

            return false;
        }

        /// <summary>把半径展开成方形范围格子（相对坐标）。</summary>
        private static Vector2Int[] BuildSquareRange(int radius)
        {
            radius = Mathf.Max(0, radius);
            int size = radius * 2 + 1;
            var cells = new List<Vector2Int>(size * size);
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }

            return cells.ToArray();
        }

        private bool TryDeploy(
            BattleController controller,
            OperatorConfig config,
            Vector2Int cell,
            HashSet<Vector2Int> usedCells)
        {
            if (usedCells.Contains(cell) || !controller.CanDeployAt(config, cell))
            {
                return false;
            }

            if (!controller.TryDeploy(config, cell, GridFacing.Right))
            {
                return false;
            }

            usedCells.Add(cell);
            _deployed.Add($"{config.operatorId}@({cell.x},{cell.y})");
            return true;
        }

        private static Vector2Int FindFallbackCell(
            BattleController controller,
            OperatorConfig config,
            HashSet<Vector2Int> usedCells)
        {
            BattleMap map = controller.Map;
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!usedCells.Contains(cell) && controller.CanDeployAt(config, cell))
                    {
                        return cell;
                    }
                }
            }

            return new Vector2Int(-1, -1);
        }

        private void SelectOperator(BattleController controller)
        {
            if (controller.Allies == null || controller.Allies.Count == 0)
            {
                return;
            }

            int index = Mathf.Clamp(_selectedOperatorIndex, 0, controller.Allies.Count - 1);
            CombatEntity ally = controller.Allies[index];
            controller.SelectAlly(ally);

            if (UIManager.Instance != null)
            {
                UIManager.Instance.Open<OperatorDetailPanel>(ally);
                UIManager.Instance.Open<OperatorActionPanel>(ally);
            }
        }

        /// <summary>
        /// 技能就绪就释放，保证每次采样的技能/弹道/伤害数字负载一致。
        /// 手动技能与自动技能都走同一个 TryActivate 入口。
        /// </summary>
        private void ActivateReadySkills(BattleController controller)
        {
            IReadOnlyList<CombatEntity> allies = controller.Allies;
            for (int i = 0; i < allies.Count; i++)
            {
                CombatEntity ally = allies[i];
                if (ally == null || !ally.IsAlive || ally.Skills == null)
                {
                    continue;
                }

                if (ally.Skills.CanActivate)
                {
                    ally.Skills.TryActivate();
                }
            }
        }

        private void OnGUI()
        {
            if (!_autoRun || !_showOverlay)
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16
            };
            style.normal.textColor = Color.white;

            float elapsed = Running ? Time.realtimeSinceStartup - _startRealtime : 0f;
            int live = _controller != null ? _controller.LiveEnemies : 0;
            int peak = Result != null ? Result.peakLiveEnemies : 0;

            GUI.Box(new Rect(10f, 10f, 330f, 92f), GUIContent.none);
            GUI.Label(new Rect(20f, 16f, 310f, 24f), $"[性能基准] {_label}", style);
            GUI.Label(new Rect(20f, 40f, 310f, 24f), $"同屏敌人 {live}    计时 {elapsed:0.0}s", style);
            GUI.Label(new Rect(20f, 64f, 310f, 24f),
                Result != null ? $"完成：峰值 {peak}，见 Logs/Perf" : "采集中…", style);
        }
    }
}
