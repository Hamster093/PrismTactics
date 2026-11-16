# 1.2 性能采样：手把手操作

> 目标：拿到「有图集 / 无图集」两份 60 敌压测报告，填进 `Performance_v1.2.md`。
> 全程在 Unity 编辑器里完成，不需要命令行，不需要出 Player 包。

---

## 0. 开始前的检查

```text
□ Unity 已打开 PrismTactics 工程，Console 没有红色报错
□ 顶部菜单能看到 Tools / PrismTactics
□ Project 里存在 Assets/GameContent/UIHandDrawn/UIHandDrawnAtlas.spriteatlas
□ Project 里存在 Assets/_Project/Scenes/M1_Perf60.unity（不存在就点菜单生成）
```

> `M1_Perf60` **不需要**加入 Build Settings：测试在编辑器内按路径加载场景。
>
> 压测口径：**只加载 AOT 内容、固定部署前 4 个干员**。第 5 个干员（黑骑士）的技能来自热更，
> 放开会把「技能释放」的 GC 波动带进对比，所以压测不部署他、也不加载热更。
> 想让黑骑士回归压测时，把场景里 `Performance Benchmark` 的 `Operator Limit` 改成 0 即可。

如果第 3 项不存在，先做第 1 步。

---

## 1. 用 2048 重新生成一次图集

改过尺寸参数，必须重打一次。

```text
菜单栏：Tools → PrismTactics → 资源 → 生成 UI 图集
```

等右下角转圈结束，Console 应该出现：

```text
[UIAtlas] 收录 35 个 Sprite，跳过 14 个（立绘/背景/非 Sprite）。图集：.../UIHandDrawnAtlas.spriteatlas
```

（Console 里那堆 `Source Texture ... compressed format` 是提示，不是错误，忽略。）

---

## 2. 采样 A：图集后（after）

### 2.1 打开测试面板

```text
菜单栏：Window → General → Test Runner
```

打开后点顶部 **PlayMode** 标签页（不是 EditMode）。

### 2.2 只跑这一个测试

在列表里找到：

```text
PerformanceBenchmarkTests
  └─ Perf60_RunsAndWritesReport
```

**右键点这一行 → Run Selected**。

> 不要点 Run All：那会把冒烟测试也跑一遍，浪费时间。

### 2.3 等待

Unity 会自动进入 Play 模式 → 加载 60 敌场景 → 部署 4 个干员 → 采样 20 秒 → 自动退出 Play。

整个过程约 **60~90 秒**，不要点鼠标、不要切窗口（切了也没事，代码里开了 runInBackground）。

结束时该测试行变绿 ✅。

### 2.4 找到报告

打开文件夹：

```text
D:\Unity\UnityLearningProject\PrismTactics\Logs\Perf\
```

按修改时间排序，最新的一对就是本次：

```text
perf_baseline_20261109_181500.json
perf_baseline_20261109_181500.md      ← 这个是人能读的
```

### 2.5 归档

把 `.md` 和 `.json` 复制到：

```text
Docs\PerfData\
```

并改名为：

```text
p1_1.2_after_atlas.md
p1_1.2_after_atlas.json
```

> 想省掉改名：跑之前打开 `Assets/_Project/Scenes/M1_Perf60.unity`，Hierarchy 里选
> `Performance Benchmark`，Inspector 的 `Label` 改成 `1.2_after_atlas`，Ctrl+S 保存，再跑测试。
> 文件名就会直接带这个标签。

---

## 3. 采样 B：图集前（before）

### 3.1 让图集"消失"

在 Project 窗口进到：

```text
Assets → GameContent → UIHandDrawn
```

选中 `UIHandDrawnAtlas.spriteatlas` → 右键 → **Rename** → 改成：

```text
UIHandDrawnAtlas.spriteatlas.bak
```

回车，等 Unity 右下角重新导入结束。

### 3.2 再跑一次测试

回到 Test Runner → 右键 `Perf60_RunsAndWritesReport` → **Run Selected**。

这次 Console 应该出现一行警告，说明回退生效了：

```text
[UIAtlas] 图集未加载，回退单图加载：UIHandDrawn/UIHandDrawnAtlas
```

> 看到这行才说明"无图集"这次是真的没加载图集。没看到就重跑。

### 3.3 归档

同样在 `Logs\Perf\` 找最新报告，复制到 `Docs\PerfData\`：

```text
p1_1.2_before_atlas.md
p1_1.2_before_atlas.json
```

---

## 4. 恢复图集

回到 Project 窗口，把文件名改回来：

```text
UIHandDrawnAtlas.spriteatlas.bak  →  UIHandDrawnAtlas.spriteatlas
```

等导入结束，再进一次 Play（随便哪个场景），确认 Console **没有** "图集未加载" 警告。

---

## 5. 发我什么

最少：两份 `.md` 里的 `## 峰值窗口` 表格，例如：

```markdown
| 指标 | 平均 | P95 | 最大 | 单位 |
|---|---|---|---|---|
| Draw Calls | ... | ... | ... | 次 |
| SetPass Calls | ... | ... | ... | 次 |
| Batches | ... | ... | ... | 次 |
| GC 分配 | ... | ... | ... | B |
| 主线程帧时间 | ... | ... | ... | ms |
```

直接把两个 md 文件放进 `Docs/PerfData/` 也行，我读文件填表。

可选加分项：跑测试时用 Frame Debugger 截一张 UI 批次的图，放进 `Docs/PerfScreenshots/`。

---

## 6. 常见问题

| 现象 | 原因 / 处理 |
|---|---|
| Test Runner 列表是空的 | 还在编译，等 Console 出现 "Reloading assemblies" 结束 |
| 跑完没变绿、报 "基准场景未在 90 秒内完成" | 上一次 Play 残留了 `Time.timeScale=0`；退出 Play 重跑 |
| `Logs\Perf` 目录不存在 | 第一次跑才会创建，跑完再看 |
| 文件名一直是 `baseline` | 没改 Label，按时间戳区分即可，不影响数据 |
| 报告里 Draw Calls 显示"不可用" | ProfilerRecorder 指标没拿到，需要在 Play 时保持 Profiler 面板关闭重跑；编辑器偶发，重跑一次通常恢复 |
| 找不到 `M1_Perf60` 场景 | 菜单 `Tools → PrismTactics → 生成 60 敌人性能基准场景` 重新生成 |
| 报 `Scene 'M1_Perf60' couldn't be loaded ... build settings` | 旧版测试才有；现在按路径加载，不需要 Build Settings，更新代码即可 |
| 报 `技能 effectKey 没有注册处理器：frost_nova` | 说明部署了黑骑士但没加载热更。压测应固定 4 个干员（`Operator Limit = 4`），或先构建 HotUpdateRes |
| 报告里只有 4 个干员 | 正常，压测固定前 4 个 AOT 干员 |

---

## 7. 取 Draw Calls / Batches（图集证据）

自动报告在编辑器里采不到渲染统计（值是 0），改用 Game 视图的 Stats 面板手动读，门槛很低。

**做法（图集在）**

1. 打开 Assets/_Project/Scenes/Bootstrap.unity，点 Play
2. 停在**选关界面**（图标最密集，不用打完整局）
3. Game 视图右上角点 **Stats**（或菜单 Window → Analysis → Statistics）
4. 截图，记下两行：

   ```text
   Batches:        xx
   SetPass calls:  xx
   ```

**做法（关掉图集）**

5. 退出 Play，菜单 `Tools → PrismTactics → 诊断 → 禁用 UI 图集（编辑器内）`
6. 再 Play，同样停在选关界面，读 Stats 并截图
7. 再次点同一个菜单恢复图集

> 不要用"把 .spriteatlas 移出 Assets"的方式做对比：移出/移回会让 Unity 重新导入并改写 meta，
> 图集可能直接失效（我们踩过一次）。用菜单开关，只影响运行时取图，不动资源文件。

**判读**：图集生效时，UI 图标会合并到 1~2 个批次，Batches 应该明显下降；SetPass calls 通常也会降。

> Stats 是"当前帧"的数字，会随界面动画小幅波动；两次都在同一界面静止 2 秒再读即可。
