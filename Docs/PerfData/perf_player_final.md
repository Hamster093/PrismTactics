# PrismTactics 60 敌人性能采样

- 标签：`player_final_2`
- 场景：`M1_Perf60`
- Unity：`2022.3.62f2c1`
- 平台：`WindowsPlayer`
- 采样时间：2026-10-31 13:03:57
- 总敌人数：60
- 峰值同屏敌人：**60**
- 采样帧数：1201

## 峰值窗口（同屏 ≥ 峰值 80%）

帧数 358，时长 5.95s，平均 60.2 FPS，窗口内峰值同屏 60，Resources.Load 0.00 次/帧。

| 指标 | 平均 | P95 | 最大 | 单位 | 数据源 |
|---|---|---|---|---|---|
| 实测帧间隔 | 16.668 | 17.026 | 20.774 | ms | 采样时间戳差值 |
| 主线程帧时间 | 0.344 | 0.618 | 4.368 | ms | PlayerLoop |
| GC 分配 | 392.458 | 300.000 | 42000.000 | B | GC.Alloc（字节） |
| Draw Calls | 0.000 | 0.000 | 0.000 | 次 | Draw Calls Count |
| SetPass Calls | 0.000 | 0.000 | 0.000 | 次 | SetPass Calls Count |
| Batches | 0.000 | 0.000 | 0.000 | 次 | Batches Count |
| Canvas 提交 | 0.000 | 0.000 | 0.000 | ms | UGUI.Rendering.UpdateBatches |
| Canvas 合批 | 0.000 | 0.000 | 0.000 | ms | Canvas.BuildBatch |
| Resources.Load / 帧 | 0.000 | 0.000 | 0.000 | 次 | ResourcesLoadTracker |

## 全程

帧数 1201，时长 20.00s，平均 60.0 FPS，窗口内峰值同屏 60，Resources.Load 0.00 次/帧。

| 指标 | 平均 | P95 | 最大 | 单位 | 数据源 |
|---|---|---|---|---|---|
| 实测帧间隔 | 16.655 | 16.981 | 27.601 | ms | 采样时间戳差值 |
| 主线程帧时间 | 0.306 | 0.542 | 11.076 | ms | PlayerLoop |
| GC 分配 | 713.405 | 300.000 | 183600.000 | B | GC.Alloc（字节） |
| Draw Calls | 0.217 | 0.000 | 147.000 | 次 | Draw Calls Count |
| SetPass Calls | 0.056 | 0.000 | 37.000 | 次 | SetPass Calls Count |
| Batches | 0.217 | 0.000 | 147.000 | 次 | Batches Count |
| Canvas 提交 | 0.000 | 0.000 | 0.000 | ms | UGUI.Rendering.UpdateBatches |
| Canvas 合批 | 0.000 | 0.000 | 0.000 | ms | Canvas.BuildBatch |
| Resources.Load / 帧 | 0.000 | 0.000 | 0.000 | 次 | ResourcesLoadTracker |

## 说明
- 峰值窗口用于排除出怪前的空场帧；阈值 = 峰值同屏数 × 80%。
- Draw Calls / SetPass / Batches 需要启用渲染设备；batchmode 下若不可用，报告会标记为 0。
- 血条池：本局创建 0 套，回收后池内 62 套。
- 弹道池：本局创建 0 发，回收后池内 8 发。
