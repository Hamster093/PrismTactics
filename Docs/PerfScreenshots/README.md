# 性能截图证据

这个目录放编辑器 Profiler 的手动截图，用于补充自动报告在 batchmode 下拿不到的渲染/UI 数据。

## 命名

```text
before_cpu.png        优化前：CPU 时间轴 + PlayerLoop 展开
after_cpu.png         优化后：CPU 时间轴 + PlayerLoop 展开
before_gc.png         优化前：GC 分配（稳态帧）
after_gc.png          优化后：GC 分配（Instantiate 尖峰帧）
after_gc_steady.png   优化后：GC 分配（稳态帧，和 before_gc 对比用）
before_rendering.png  优化前：Rendering 模块（Draw Calls / SetPass / Batches）
after_rendering.png   优化后：Rendering 模块
before_ui.png         优化前：UI 模块 / Canvas 批次
after_ui.png          优化后：UI 模块 / Canvas 批次
```

## 拍摄步骤（详细版见 `Docs/Performance_v1.1.md` 第 7 节）

1. 打开 `Assets/_Project/Scenes/M1_Perf60.unity`。
2. `Window > Analysis > Profiler`，选择 Play Mode，关闭 Deep Profile，打开 CPU / GC Alloc / Rendering / UI 模块。
3. 选中 `Performance Benchmark`：
   - 采“优化后”：不勾“旧版 HUD 轮询（对照用）”
   - 采“优化前”：勾上
4. Play，等同屏敌人接近 60（约第 5~9 秒），在 Profiler 时间轴上点一帧暂停。
5. 按命名规则截图。
6. 停止 Play；采“优化前”后记得取消勾选，避免污染后续测试。

## 记录表

| 截图 | 采集时间 | 同屏敌人 | 关键数据 / 备注 |
|---|---|---|---|
| before_cpu | 2026-10-31 12:34 | 60 | 第 1009 帧；PlayerLoop 1.81 ms；BattleHudBinder.Update 0.06 ms / 0.7 KB |
| after_cpu | 2026-10-31 12:01 | 60 | 第 747 帧；PlayerLoop 2.52 ms；EditorLoop 3.38 ms（编辑器波动，横比不看总时间） |
| before_gc | 2026-10-31 12:35 | 60 | 第 1009 帧；PlayerLoop GC 2.0 KB，其中 HUD 0.7 KB / 23 次，GUI.Repaint 1.0 KB |
| after_gc | 2026-10-31 12:13 | 60 | 第 550 帧；Instantiate 6 次 49 KB + GC.Alloc 5.4 KB，用来证明“尖峰来自创建对象” |
| after_gc_steady | 2026-10-31 12:22 | 60 | 稳态帧；PlayerLoop GC 1.0 KB，HUD 分配消失，剩余主要是基准浮层 |
| before_rendering | 2026-10-31 12:35 | 60 | 第 1015 帧；Draw Calls 155 / SetPass 40 / Batches 155 / Textures 28 |
| after_rendering | 2026-10-31 12:07 | 60 | 第 616 帧；Draw Calls 131 / SetPass 33 / Batches 131 / Textures 14 |
| before_ui | 2026-10-31 12:36 | 60 | 第 1015 帧；主 Canvas 21 batches / 516 vertices |
| after_ui | 2026-10-31 12:07 | 60 | 第 616 帧；选中帧没有 UI 重建，只剩 Damage Number Canvas 0 batches |

## 关键观察

1. **HUD 每帧分配消失**：优化前稳态帧里 `BattleHudBinder.Update` 每帧 0.7 KB（23 次 GC.Alloc）；优化后稳态帧 PlayerLoop 只剩 1.0 KB，主要是基准浮层的 `GUI.Repaint`，HUD 不再产生每帧垃圾。
2. **UI 重建下降**：优化前主 Canvas 21 batches / 516 vertices；优化后选中帧主 Canvas 没有重建。
3. **GC 尖峰来自 Instantiate**：`after_gc.png` 第 550 帧正好遇到 6 次 Instantiate，单帧 49 KB；这是“敌人/伤害数字对象池”的下一步依据，不代表平均帧。
4. **渲染还有空间**：Draw Calls 131、Batches 131，Dynamic Batching 只有 82 calls / 4 batches；图集与共享材质是下一阶段目标。

> 注意：截图来自不同帧，CPU 总时间受 EditorLoop 影响，不能拿 `after_cpu` 的 2.52 ms 和 `before_cpu` 的 1.81 ms 直接比大小；要看具体条目（HUD、UI、GC）。

## 读图提示

- Profiler 打开本身有开销，前后两次必须用同样设置。
- Draw Calls 包含编辑器自身 UI，绝对值与真机不同，看前后相对变化。
- `Canvas.SendWillRenderCanvases` / `UGUI.Rendering.UpdateBatches` 在没有 UI 变化的帧应接近 0。
