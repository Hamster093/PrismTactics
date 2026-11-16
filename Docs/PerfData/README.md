# 性能采样原始数据

这些文件由 `M1_Perf60` 基准场景自动生成，源文件在 `Logs/Perf/`（Logs 被 .gitignore 忽略），这里是纳入仓库的证据副本。

| 文件 | 标签 | 说明 |
|---|---|---|
| `perf_baseline.json` / `.md` | `baseline` | 1.0 原始 HUD（每帧轮询）的基线 |
| `perf_legacy_control.json` / `.md` | `legacy_control` | 同一构建里通过“旧版 HUD 轮询（对照用）”复现的对照 |
| `perf_after_all.json` / `.md` | `after_all` | HUD 事件化 + Spine 缓存 + 弹道/血条池后的结果（第 1 次） |
| `perf_after_all_2.json` | `after_all_2` | 优化后第 2 次复测，用于确认波动 |
| `perf_after_hud_split.json` / `.md` | `after_hud_split` | 再拆分属性/生命/SP 刷新后的第 1 次 |
| `perf_after_hud_split_2.json` | `after_hud_split_2` | 拆分后第 2 次复测 |
| `perf_after_pool_warm.json` / `.md` | `after_pool_warm` | 进战预热 + 路线缓存后（最终编辑器数据：GC 218.2 B/帧，主线程 0.289 ms） |
| `perf_player_baseline.json` / `.md` | `player_baseline` | 预热前的 Development Player 采样 |
| `perf_player_final.json` / `.md` | `player_final` | 最终代码的 Player 采样（Player GC 受尖峰影响，两次为 318/392 B/帧，P95 300~400） |

## 场景条件

- 场景 `M1_Perf60`，地图 `M1_Perf60`
- 3 条路线各 20 个 4 号敌人，约 4.8 秒出完，峰值同屏 60
- 固定部署 4 个干员：`(5,3)`、`(7,1)`、`(3,3)`、`(4,1)`
- 技能就绪自动释放
- 60 帧封顶、VSync 关闭、1 倍速、采样 20 秒
- 峰值窗口：同屏敌人 ≥ 峰值 × 80%

## 复现

见 `Docs/Performance_v1.1.md` 第 6 节。报告里的标签可用 `PRISM_PERF_LABEL` 环境变量覆盖，不需要改场景。

```powershell
$env:PRISM_PERF_LABEL="after_hud"
```

## 注意

- 数据来自 Windows 编辑器 + batchmode，没有渲染设备，所以 Draw Calls / SetPass / Batches / UI 重建为 0。
- 渲染和 UI 重建证据用编辑器 Profiler 手动截图，放到 `Docs/PerfScreenshots/`。
- 原始基线只跑了 1 次；优化后分两阶段各跑 2 次，GC 在 244~274 B/帧之间波动，主线程在 0.34~0.46 ms 之间波动，趋势一致。
