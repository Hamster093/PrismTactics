# PrismTactics 性能基线与优化（1.1）

日期：2026-10-30  
工程：`D:\Unity\UnityLearningProject\PrismTactics`  
Unity：`2022.3.62f2c1`（中国版）  
场景：`Assets/_Project/Scenes/M1_Perf60.unity`

## 0. 一句话结论

在固定的 60 敌人峰值场景里，把 HUD 从“每帧轮询”改成“事件驱动 + 缓存”，再拆分属性/生命/SP 的刷新粒度，并补上弹道/血条对象池：

| 指标（峰值窗口平均） | 优化前 | 优化后 | 变化 |
|---|---:|---:|---:|
| GC 分配 | 748.9 B/帧 | 218.2 B/帧 | **-71%** |
| GC 分配 P95 | 1000 B | 300 B | **-70%** |
| `Resources.Load` | 2.00 次/帧 | 0.00 次/帧 | **-100%** |
| 主线程帧时间 | 0.500 ms | 0.289 ms | **-42%** |
| 主线程 P95 | 0.840 ms | 0.458 ms | **-45%** |
| 平均帧率（60 帧封顶） | 60.2 FPS | 60.2 FPS | 不变（预期） |

> 帧率不变不是优化没效果：本机在 60 帧封顶下，战斗主线程只占 0.29 ms，离 16.67 ms 的预算很远。优化目标是降低每帧 CPU/GC 成本、给同屏单位留余量，而不是把 60 帧提高到 61 帧。

---

## 1. 为什么要先做基线

面试里说“我做了性能优化”很容易，但如果没有可复现的数据，面试官会追问：

- 优化前是多少？优化后是多少？
- 同屏多少单位？什么场景？什么指标？
- 为什么这个改动会有效？怎么证明不是环境波动？

所以这一步的顺序是：

```text
冻结场景和波次
-> 固定部署和技能释放
-> 先记录优化前数据
-> 只改一个方向，复测
-> 用同一套指标对比
```

本项目的基线不是“故意写差代码”造出来的：1.0 的 HUD 本来就是每帧 `Refresh()`，我们只是把它冻结下来，量出真实成本。

---

## 2. 压测场景

### 2.1 场景与配置

- 场景：`Assets/_Project/Scenes/M1_Perf60.unity`（从 `M1_Battle` 复制后裁剪，独立场景）
- 地图：`Assets/StreamingAssets/Configs/map_M1_Perf60.json`（`map_1.json` 的副本，`mapId = M1_Perf60`）
- 波次：`waves_M1_Perf60.json`
  - 3 条路线各 20 个 4 号敌人（冰焰骷髅，200 血、速度 1.5）
  - 开局 1 秒后开始，0.2 秒间隔，约 4.8 秒内出完 60 个
- 战斗参数：`battle_M1_Perf60.json`
  - 初始生命 999、初始费用 99、费用回复 1/s
  - 生命拉高是为了不在采样中途战败，费用拉高是为了能一次部署 4 个干员
- 干员：直接读当前 `operators.json`，按顺序固定部署在 4 个格子：

| 顺位 | 部署格 | 说明 |
|---|---|---|
| 1 | `(5, 3)` | 高台，覆盖两条路的交汇区 |
| 2 | `(7, 1)` | 高台，覆盖上方路线 |
| 3 | `(3, 3)` | 地面，阻挡位 |
| 4 | `(4, 1)` | 地面，阻挡位 |

- 技能：驱动器在 `SkillComponent.CanActivate` 为 true 时自动释放，保证每次采样的技能/弹道/伤害数字负载一致。
- 相机：沿用 `M1_Battle` 的 Main Camera，基准场景复制后不再改。
- 帧率：`Application.targetFrameRate = 60`、`vSyncCount = 0`，让帧间隔稳定在 16.67 ms，便于对比“每帧成本”。

### 2.2 采样窗口

采样从部署完 4 个干员后开始，持续 20 秒；HUD 同时显示同屏敌人数和计时。

报告里有两组汇总：

- **全程**：20 秒所有帧。
- **峰值窗口**：只取“同屏敌人数 ≥ 峰值 × 80%”的帧（本场景峰值 60，即同屏 ≥ 48，约 5.95 秒 / 358 帧）。

为什么要峰值窗口：出怪前有几秒空场，如果把空场帧一起平均，会把成本稀释掉。峰值窗口更接近“压力最大的那几秒”。

---

## 3. 指标是什么意思

| 指标 | 单位 | 怎么来的 | 面试里怎么解释 |
|---|---|---|---|
| 实测帧间隔 | ms | 相邻两帧采样时间戳差值 | 实际帧节奏，本场景受 60 帧封顶影响 |
| 主线程帧时间 | ms | ProfilerRecorder `PlayerLoop` | 一帧里主线程真正干活的时间，排除等垂直同步 |
| GC 分配 | B/帧 | ProfilerRecorder `GC.Alloc` | 每帧新分配多少托管内存，越低越不容易触发 GC 卡顿 |
| `Resources.Load` | 次/帧 | `ResourcesLoadTracker` 包一层计数 | 是否在战斗热路径反复走资源系统 |
| Draw Calls | 次 | ProfilerRecorder `Draw Calls Count` | 一帧提交多少次绘制，受材质/图集影响 |
| SetPass Calls | 次 | ProfilerRecorder `SetPass Calls Count` | 切换渲染状态的次数 |
| Batches | 次 | ProfilerRecorder `Batches Count` | 合批后的批次数量 |
| Canvas 提交 / 合批 | ms | `UGUI.Rendering.UpdateBatches` / `Canvas.BuildBatch` | UGUI 这一帧有没有重建网格/批次 |

> **batchmode 的限制**：自动化测试跑在 `-batchmode` 下没有渲染设备，Draw Calls / SetPass / Batches / UI 重建会记录为 0。所以这两类数据用**编辑器 Profiler 手动截图**补，步骤见第 6 节。

### 3.1 峰值报告长什么样

自动生成的报告在：

```text
Logs/Perf/perf_<标签>_<时间戳>.json
Logs/Perf/perf_<标签>_<时间戳>.md
Logs/Perf/perf_<标签>_latest.json / .md
```

整理进仓库的证据副本在 `Docs/PerfData/`：

```text
perf_baseline.json / .md          # 优化前（1.0 原始 HUD）
perf_legacy_control.json / .md    # A/B 对照：同一构建里切回旧轮询
perf_after_all.json / .md         # 优化后
perf_after_all_2.json             # 优化后第二次复测
```

报告里会写：

- 场景、Unity 版本、平台、采样时间
- 峰值同屏敌人数、总敌人数、采样帧数
- 峰值窗口和全程的 avg / P95 / max
- 指标数据源（例如 `PlayerLoop`、`GC.Alloc`、`ResourcesLoadTracker`）
- 血条池 / 弹道池本局创建数量

---

## 4. 优化前后数据

### 4.1 主要结果（峰值窗口）

| 指标 | 优化前（1.0） | A/B 对照（旧轮询） | HUD 事件化后 | HUD 拆分后 | 对象池预热后 |
|---|---:|---:|---:|---:|---:|
| 峰值同屏敌人 | 60 | 60 | 60 | 60 | 60 |
| 峰值窗口帧数 | 358 | 358 | 358 | 358 | 358 |
| 平均帧率 | 60.2 | 60.2 | 60.2 | 60.2 | 60.2 |
| 实测帧间隔 | 16.667 ms | 16.667 ms | 16.667 ms | 16.667 ms | 16.667 ms |
| 主线程帧时间 | 0.500 ms | 0.573 ms | 0.426 ms | 0.343~0.347 ms | **0.289 ms** |
| 主线程 P95 | 0.840 ms | 1.069 ms | 0.809 ms | 0.536~0.562 ms | **0.458 ms** |
| GC 分配 | 748.9 B/帧 | 847.5 B/帧 | 269.8 B/帧 | 244.4~258.7 B/帧 | **218.2 B/帧** |
| GC 分配 P95 | 1000 B | 1200 B | 400 B | 300~400 B | **300 B** |
| `Resources.Load` | 2.00 次/帧 | 2.00 次/帧 | 0.00 次/帧 | 0.00 次/帧 | 0.00 次/帧 |

- 原始基线是 1.0 代码直接跑的；A/B 对照是在同一份新构建里，把 `Performance Benchmark` 的“旧版 HUD 轮询（对照用）”勾上跑的。
- 对照值比原始基线略高，是因为对照模式除了每帧刷新，还额外清缓存、并且事件监听仍然存在；它证明“旧轮询的成本量级”可以复现，绝对值以原始基线为准。
- 每阶段都复测 2 次，数字有波动，表里用区间或最终值；GC 平均值受偶发 Instantiate 尖峰影响较大，所以同时看 P95。
- A/B 对照（旧轮询）只保留了一次采样，P95 取自 `Docs/PerfData/perf_legacy_control.md`（GC 1200 B、主线程 1.069 ms），只用来证明旧轮询的成本量级，不作为最终结论。
- 对象池预热是在进战初始化时预创建血条/弹道/飘字，把高峰的 Instantiate 成本提前；不是凭空消失，峰值窗口里不再集中发生。

### 4.2 对象池效果

优化后报告里的池统计：

```text
血条池：本局创建 60 套，回收后池内 62 套。
弹道池：本局创建 1 发，回收后池内 1 发。
```

- 60 个敌人同屏，血条峰值必须存在 60 套，所以“创建 60 套”是合理的；收益发生在敌人死亡、下一波复用，不需要每死一个就 `Instantiate/Destroy`。
- 弹道只创建了 1 发，之后所有远程攻击都复用同一发，说明池命中率很高（远程攻击间隔大于弹道飞行时间）。

### 4.3 为什么帧率没有变

```text
16.67 ms 帧预算
-  0.29 ms 战斗主线程
=  16.38 ms 余量
```

本场景 CPU 远没有跑满，帧率被 60 帧上限锁住。优化的价值是：

1. 把每帧 GC 从 749 B 降到 244 B，减少 GC 触发频率和潜在卡顿。
2. 把主线程成本降低 31%，同屏单位上限更高。
3. 把 `Resources.Load` 从每帧 2 次降到 0，避免资源系统在战斗热路径被反复调用。

如果面试官问“为什么不是 60 帧变 120 帧”，就用这段回答：**先看瓶颈在哪里；60 帧封顶下 CPU 不是瓶颈，优化的是 headroom 和 GC，而不是帧率数字。**

---

## 5. 具体改了什么

### 5.1 P0：HUD 事件化 + 缓存（最大收益）

**原来的问题**

`BattleHudBinder.Update` 每帧调用 `Refresh()`：

- 生命、剩余敌人、费用、剩余部署数、生命值、SP 都做字符串插值；
- 每帧对每个干员 `GetComponent<OperatorRangeView>()` 并 `SetActive`；
- 每帧 `Resources.Load` 头像和技能图标；
- 每帧设置血条、技能遮罩、暂停/倍速图片。

即使 UGUI 的 `Text.text` 会判重，**插值字符串本身已经分配了**，而且 `Resources.Load` 每帧都走一次资源系统。

**改法**

- `BattleController` 增加事件：
  - `LifeChanged`：漏怪扣生命时触发
  - `CostChanged`：部署扣费、费用回复时触发
  - `EnemiesChanged`：敌人加入/移除时触发
  - `SelectionChanged`：选中干员变化时触发
- HUD 在 `Initialize` 订阅事件，`OnDestroy` 退订。
- 顶部栏用 `_lastLife/_lastCost/_lastRemainingEnemies/...` 缓存，值不变就不拼字符串。
- 选中干员后订阅 `HealthComponent.HealthChanged` 和 `SkillComponent.SpChanged`，只刷新当前干员。
- 头像/技能图标用 `Dictionary<string, Sprite>` 缓存，第二次打开面板不再走 `Resources.Load`。
- 攻击范围显示只在选中变化时更新；`OperatorRangeView.SetVisible` 自己再做一层状态缓存。

**代码位置**

```text
Assets/_Project/Scripts/Battle/Runtime/BattleController.cs
Assets/_Project/Scripts/Battle/Runtime/UI/BattleHudBinder.cs
Assets/_Project/Scripts/Battle/Runtime/View/OperatorRangeView.cs
```

### 5.1.1 第二轮：拆分属性 / 生命 / SP 刷新

事件化后还留了一个隐患：`SkillComponent.SpChanged` 在持续技能期间每 tick（30Hz）触发，`HandleAllySpChanged` 每次都调用 `RefreshOperator`，把攻击/防御/法抗/阻挡/生命 5 个字符串全部重拼一遍。

改法：

- `RefreshOperator` 拆成 `RefreshOperatorStats` 和 `RefreshOperatorHealth`；
- 生命变化只更新血条 + 生命文字（并缓存上一次的整数生命，没变就不拼字符串）；
- SP 变化只更新技能文字；只有攻击力真的变了（Buff 开始/结束）才刷新属性；
- 切换选中干员时重置所有缓存。

效果：主线程 0.426 → 0.343 ms，P95 0.809 → 0.536 ms；GC P95 400 → 300 B。

### 5.2 P1：Spine 朝向 / 隐藏 Slot 缓存

**问题**

- 每个单位 `LateUpdate` 都写一次 `transform.rotation`，即使朝向没变；
- 远程单位每次 `LateUpdate` 和 `UpdateComplete` 都 `FindSlot` 找 6 个隐藏 Slot。

**改法**

- 缓存上次写入的 yaw，只有左右翻转时才写 transform。
- 缓存 `Skeleton` 对应的 `Slot[]`，只有 Skeleton 引用变化（重建）时才重新查找。

**代码位置**：`Assets/_Project/Scripts/Battle/Runtime/View/SpineUnitView.cs`

### 5.3 P2：弹道对象池

**问题**：每发子弹 `Instantiate`，命中 `Destroy`，远程干员持续攻击时反复产生 GC 和实例化开销。

**改法**：

- `ProjectileManager` 维护 `Stack<ProjectileView>`；
- 命中/目标失效时 `Release`：清引用、`SetActive(false)`、入池；
- 下次发射优先从池里取，池空了才 `Instantiate`；
- 池对象在切场景时被 Unity 销毁，取用时跳过 null 引用。

**代码位置**：

```text
Assets/_Project/Scripts/Battle/Runtime/UI/Combat/ProjectileManager.cs
Assets/_Project/Scripts/Battle/Runtime/UI/Combat/ProjectileView.cs
```

### 5.4 P3：血条 / 技力条对象池

**问题**：每个敌人创建时 `Instantiate(HealthBar.prefab)`，死亡时随单位一起 `Destroy`。

**改法**：

- `UnitStatusBarFactory` 维护血条池；
- 单位销毁前调用 `Release`：解绑 `HealthBarView` / `SpCostBarView`、从单位根节点摘下、失活、入池；
- 复用时重置 localPosition / localRotation / localScale，重新朝向相机并绑定新实体。

**代码位置**：

```text
Assets/_Project/Scripts/Battle/Runtime/UI/Combat/UnitStatusBarFactory.cs
Assets/_Project/Scripts/Battle/Runtime/BattleController.cs（死亡/撤退/移除敌人时 Release）
```

### 5.4.1 对象池预热 + 路线缓存（第四步）

**问题**：第一波 60 个敌人集中出生时，血条、飘字、弹道会临时 Instantiate，形成 GC 尖峰（截图里 6 次 Instantiate / 49 KB）；每个敌人出生还会复制一份路线 List、重复校验/寻路。

**改法**：

- `M1BattleBootstrap` 进战时预热：血条 20 套、弹道 8 发、飘字 24 个；
  （性能报告那次采样用的是 64 套血条；后续按设计改为 20 套，未重新采样。）
- `WaveManager` 用 `Dictionary<string, List<Vector2Int>>` 缓存路线，同一 routeId 只解析一次，敌人共享只读路线；
- 池在切场景后会有已销毁的 null 引用，预热前先清理 null，避免误判池容量。

**效果**：峰值窗口 GC 244.4 → 218.2 B/帧，主线程 0.347 → 0.289 ms，`max` 从 15.5 KB 降到 13.3 KB；报告里“血条池创建 0 套 / 弹道池创建 0 发”说明全部命中预热对象。

> 这一步只做了“预热 + 路线缓存”，**完整的敌人实例池**没有做：它要改 EnemyUnit/CombatEntity 的生命周期、事件解绑和状态复位，风险高，建议单独一轮配更完整的回归测试。

### 5.5 性能工具（新增，不属于游戏逻辑）

```text
Assets/_Project/Scripts/Battle/Runtime/Diagnostics/
├── BattlePerformanceProbe.cs        # 逐帧采样 + 峰值窗口 + 写 JSON/Markdown
├── PerformanceBenchmarkRunner.cs    # 固定部署、自动技能、20 秒采样、A/B 开关
└── ResourcesLoadTracker.cs          # Resources.Load 计数

Assets/_Project/Scripts/Combat/Editor/
├── PerfBenchmarkSceneBuilder.cs     # 从 M1_Battle 生成 M1_Perf60（只读原场景）
└── ProfilerStatProbe.cs             # 枚举可用 Profiler 指标，排查指标名/单位

Assets/_Project/Scripts/Tests/PlayMode/PerformanceBenchmarkTests.cs
Assets/StreamingAssets/Configs/{map,waves,battle}_M1_Perf60.json
```

---

## 6. 怎么复现

### 6.1 手动在编辑器里跑（推荐先做一次）

1. 用 Unity `2022.3.62f2c1` 打开工程。
2. 打开 `Assets/_Project/Scenes/M1_Perf60.unity`。
3. 场景里只有一个基准相关对象：`Performance Benchmark`。
4. 选中它，Inspector 里能看到：
   - `Label`：报告标签，默认 `baseline`
   - `Sample Seconds`：采样时长，默认 20
   - `Auto Activate Skills`：自动开技能，默认勾选
   - `Target Frame Rate`：默认 60
   - `旧版 HUD 轮询（对照用）`：默认不勾；勾上就是优化前的行为
5. 点击 Play。
6. 左上角会出现黑色信息框：同屏敌人数、计时、完成后提示。
7. 约 20 秒后 Console 输出：

```text
[Perf] 采样结束：峰值同屏 60 个敌人；峰值窗口平均 60.2 FPS；报告 ...Logs/Perf/perf_baseline_....json
```

8. 报告在 `Logs/Perf/`；把 `perf_<label>_latest.md` 打开就能看到表格。

### 6.2 命令行自动跑（用于前后对比/回归）

```powershell
& "D:\Unity\2022.3.62f2c1\Editor\Unity.exe" `
  -batchmode `
  -projectPath "D:\Unity\UnityLearningProject\PrismTactics" `
  -runTests -testPlatform PlayMode `
  -testResults "Logs\perf_tests.xml" `
  -logFile "Logs\perf.log"
```

- 只跑基准测试也可以：在 Test Runner 里筛选 `PerformanceBenchmarkTests`。
- 用环境变量区分标签，不需要改场景：

```powershell
$env:PRISM_PERF_LABEL="after_hud"
```

- 用环境变量跑“旧轮询对照”：

```powershell
$env:PRISM_PERF_LEGACY_HUD="1"
```

- 跑完记得清掉：

```powershell
Remove-Item Env:PRISM_PERF_LABEL,Env:PRISM_PERF_LEGACY_HUD -ErrorAction SilentlyContinue
```

### 6.3 重新生成基准场景

菜单：`Tools > PrismTactics > 生成 60 敌人性能基准场景`

它只做四件事：

1. 复制 `M1_Battle.unity` 为 `M1_Perf60.unity`；
2. 把 Bootstrap 的 Map Id 改成 `M1_Perf60`、Auto Build 打开；
3. 删掉选关/选技能/背包/掉落/结束面板和 Meta 流程；
4. 加 `Performance Benchmark` 驱动器，并加入 Build Settings。

> 不会修改 `M1_Battle.unity`。再次强调：不要用旧的 M1SceneBuilder 覆盖主场景。

### 6.4 Player 基线（自动化）

目的：验证“编辑器里的结论在打包后的 Player 里也成立”。

1. 菜单：`Tools > PrismTactics > 性能基准 > 构建 Player（60 敌人）`
   - 命令行：`-executeMethod PrismTactics.EditorTools.PerfPlayerBuilder.BuildFromCli`
   - 只打 `M1_Perf60` 一个场景，输出 `Builds/Perf60/PrismTacticsPerf.exe`
2. 运行：

```powershell
cd Builds\Perf60
.\PrismTacticsPerf.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -perfQuit
```

- `-perfQuit` 让基准跑完自动退出，方便脚本采集。
- 报告写到 `Builds/Perf60/Logs/Perf/`，副本在 `Docs/PerfData/perf_player_baseline.*`。

Player 结果（峰值窗口）：

| 指标 | 编辑器（最终） | Player（两次范围） |
|---|---:|---:|
| 主线程帧时间 | 0.289 ms | **0.263~0.344 ms** |
| 主线程 P95 | 0.458 ms | 0.611~0.621 ms |
| GC 分配 | 218.2 B/帧 | **222~392 B/帧** |
| GC 分配 P95 | 300 B | 300~400 B |
| 平均帧率 | 60.2 FPS | 60.1~60.2 FPS |

说明：

- Player 没有 `EditorLoop`，所以主线程平均值更低；P95 略高是因为 Player 的采样尖峰不同。
- Player 的 GC 平均值受偶发 Instantiate 尖峰影响，两次跑出 222 和 392 B/帧；P95 稳定在 300~400，和编辑器结论一致。
- Player 里 `ProfilerRecorder` 的 Draw Calls / SetPass / Batches 默认不可用（需要连接 Profiler），报告里是 0；渲染数据以编辑器 Profiler 截图为准（Draw Calls 131 / SetPass 33）。
- 这是 Development Build，和正式发布包仍有差异，但比编辑器更接近真机。

---

## 7. 用 Profiler 抓“前后对比”截图

batchmode 拿不到 Draw Calls / UI 重建，所以这部分用编辑器 Profiler 手动取证。步骤尽量固定，保证前后可比。

### 7.1 准备

1. 打开 `M1_Perf60` 场景。
2. 打开 `Window > Analysis > Profiler`。
3. Profiler 顶部选择 `Play Mode`。
4. **关闭 Deep Profile**（Deep Profile 会给每个函数插桩，数据会失真）。
5. 右侧模块建议至少打开：
   - `CPU Usage`
   - `GC Alloc`
   - `Rendering`
   - `UI`（如果没有 UI 模块，就在 CPU 层级里看 `Canvas.*` / `UGUI.*`）

### 7.2 采“优化后”

1. 确认 `Performance Benchmark` 的“旧版 HUD 轮询（对照用）”**不勾选**。
2. 点击 Play。
3. 等 HUD 显示同屏敌人接近 60（大约第 5~9 秒），点击 Profiler 时间轴上的某一帧，让时间轴停在这一帧。
4. 截图：
   - CPU 时间轴：`Docs/PerfScreenshots/after_cpu.png`
   - GC Alloc 模块（按 GC Alloc 排序）：`Docs/PerfScreenshots/after_gc.png`
   - Rendering 模块（Draw Calls / SetPass / Batches）：`Docs/PerfScreenshots/after_rendering.png`
   - CPU 层级里的 `Canvas.SendWillRenderCanvases` 或 `UGUI.Rendering.UpdateBatches`：`Docs/PerfScreenshots/after_ui.png`
5. 停止 Play。

### 7.3 采“优化前”

1. 选中 `Performance Benchmark`，勾选“旧版 HUD 轮询（对照用）”。
2. 重新 Play，重复 7.2 的等待时间和截图位置（尽量也在同屏 60 的帧）。
3. 截图命名改成 `before_cpu.png`、`before_gc.png`、`before_rendering.png`、`before_ui.png`。
4. 停止 Play，把勾选取消，避免下次误用。

### 7.4 截图时怎么看

- **GC Alloc**：优化前每个帧段都有稳定的小额分配；优化后应该只在伤害数字/SP 变化等真正需要的地方出现。
- **Canvas / UGUI**：优化前每帧都有 `Canvas.SendWillRenderCanvases` / `UGUI.Rendering.UpdateBatches`；优化后选一个“没有数值变化”的帧，应该看不到或明显更短。
- **Rendering**：Draw Calls 主要由 Spine 单位、血条 Sprite、地图格子决定；HUD 事件化不会大幅降低 Draw Calls，这是正常的，下一阶段才做图集/合批。
- **CPU**：看 `PlayerLoop` 下的 `Scripts`、`Canvas`、`Rendering` 占比，而不是只看总帧时间（编辑器自身开销很大）。

> 截图结论写进 `Docs/PerfScreenshots/README.md` 或本文第 8 节，保证面试时能直接翻。

### 7.5 已采集的截图与结论

截图在 `Docs/PerfScreenshots/`，记录表和读图提示见该目录的 `README.md`。

| 对比项 | 优化前 | 优化后 |
|---|---|---|
| HUD 每帧 GC（稳态帧） | `BattleHudBinder.Update` 0.7 KB / 23 次 GC.Alloc | PlayerLoop 只剩 1.0 KB，主要是基准浮层 `GUI.Repaint` |
| 主 Canvas UI 重建 | 21 batches / 516 vertices | 选中帧没有主 Canvas 重建 |
| Draw Calls | 155 | 131 |
| SetPass Calls | 40 | 33 |
| Batches | 155 | 131 |
| Used Textures | 28 / 3.9 MB | 14 / 2.3 MB |

补充说明：

- `after_gc.png` 抓到的是 **Instantiate 尖峰帧**（6 次 / 49 KB），不是平均帧；它正好证明下一步该做敌人/伤害数字对象池。
- 前后截图来自不同帧，编辑器 `EditorLoop` 会波动，所以不比较 CPU 总时间，只比较具体条目（HUD、Canvas、GC）。
- Draw Calls 的 155 → 131 可能受同屏伤害数字/UI 元素数量影响，不作为主要结论；下一阶段用图集/合批后再正式对比。

---

## 8. 数据解读与局限

### 8.1 结论

1. **HUD 每帧轮询是主要可优化点**：GC 749 -> 218 B/帧（P95 1000 -> 300），`Resources.Load` 2 -> 0 次/帧。
2. **主线程成本同步下降**：0.500 -> 0.289 ms（-42%），P95 0.840 -> 0.458 ms（-45%）。
3. **对象池的作用**：峰值窗口里 60 个敌人同时存活，血条本来就需要 60 套；预热把第一波的 Instantiate 成本提前到进战初始化，峰值窗口里血条/弹道“本局创建 0 套/发”。
4. **帧率不变是符合预期的**：CPU 只占 0.29 ms，60 帧封顶；优化买的是余量，不是帧率数字。

### 8.1.1 剩余 GC 是什么产生的

HUD 拆分 + 对象池预热后，峰值窗口平均约 218 B/帧（P95 300 B）。和截图对照：

- 优化前稳态帧：`PlayerLoop` 2.0 KB，其中 `BattleHudBinder.Update` 0.7 KB / 23 次 `GC.Alloc`，`GUI.Repaint` 1.0 KB。
- 优化后稳态帧：`PlayerLoop` 只剩 1.0 KB，基本就是基准浮层的 `GUI.Repaint`；HUD 不再产生每帧垃圾。

剩余来源按预估占比排序：

1. **伤害数字**：每次受击 `CombatFeedback` 把伤害数字 `ToString()`；`DamageNumberManager` 每次 `+=` 一个委托，`DamageNumberView.Play` 每次 `StartCoroutine` 分配一个枚举器。量级不大，但每次受击都有。
2. **敌人出生 / 按需 Instantiate**：血条/飘字/弹道已经预热，但敌人本身还是 `new GameObject` + Spine Instantiate；`after_gc.png` 第 550 帧的 6 次 Instantiate / 49 KB 就是这一类尖峰。完整的敌人实例池是下一步。
3. **技能激活**：`ApplyTimedSkill` 会 `new StatModifier`，技能释放时才发生，频率低。
4. **基准浮层**：`PerformanceBenchmarkRunner.OnGUI` 每帧 `new GUIStyle` + 3 个字符串，截图里对应 `GUI.Repaint` 约 1 KB/帧。只存在于基准场景，正常游戏没有。
5. **编辑器/Profiler 自身**：batchmode 下 `GC.Alloc` 也会包含编辑器侧的偶发分配，所以平均值会跨会话波动（同一份代码 218~365 B/帧），P95 更稳定。

> HUD 的 5 次字符串拼接已经拆掉了；继续砍要看“受击/创建对象”这些事件，而不是每帧 UI。

### 8.2 局限

- 主数据来自 Windows 编辑器；Player 基线已有 Development Build 数据（主线程 0.263~0.344 ms、GC 222~392 B/帧、P95 300~400），但还不是正式发布包和低端机。
- batchmode 和 Player 自动采集都拿不到 Draw Calls / SetPass / Batches / UI 重建（Player 需要连接 Profiler），这四项用手动 Profiler 截图补齐。
- 只有一个 60 敌人场景，没有覆盖低端机、长时间挂机、连续换波等场景。
- 原始基线只跑了 1 次；后续每个优化阶段各跑 2 次，趋势一致，但严格论文式结论应做 3 次取中位数。
- `GC.Alloc` 平均值跨会话有约 ±30% 波动（编辑器自身分配），比较时优先看 P95 和同一时段的对照，不要死盯小数。
- 峰值窗口按“同屏 ≥ 峰值 80%”筛选，换一个阈值数值会略变，但优化前后用同一规则，对比仍然成立。

### 8.3 下一步（按收益排序）

1. 图集/合批：血条 Sprite 与 Spine 材质，目标降低 Draw Calls / SetPass（当前 131 / 33）。
   - 1.2 已完成「UI 小图标打 SpriteAtlas」这一半，数据见 `Docs/Performance_v1.2.md`；血条/Spine 材质合批仍未做。
2. 完整的敌人实例池：本场景 60 个敌人仍是 `new GameObject` + Spine Instantiate；要改生命周期、事件解绑和状态复位，建议单独一轮。
3. 低端机/正式包 Player 基线：Development Build 已完成，下一步补目标机器。
4. 网格范围查询优化：`BattleGrid.GetTargetsInRange` 的旋转范围缓存 + 去重，当前 4 干员/60 敌人占比不高，等敌人技能/更多干员再评估。
5. 把性能基准接入 CI（每次提交跑 1 次，超过阈值告警）。

---

## 9. 面试怎么讲

### 9.1 一分钟版本

> 1.0 做完后我先冻结了一个 60 敌人峰值场景，固定地图、相机、波次、部署格和技能释放，先记录基线。用 ProfilerRecorder 抓主线程、GC、Resources.Load，峰值窗口取同屏 60 的那 358 帧。
>
> 定位到 HUD 每帧 `Refresh()` 是主要 GC 来源：每帧字符串插值、每帧 `Resources.Load` 头像和技能图标、每帧对每个干员 GetComponent。改成事件驱动 + 值缓存 + 图标缓存后，又把属性/生命/SP 的刷新粒度拆开，GC 从 749 降到 218 B/帧（-71%，P95 1000→300），Resources.Load 从 2 次/帧降到 0，主线程从 0.50 降到 0.29 ms（-42%）。
>
> 又补了弹道池、血条池和进战预热，并把路线缓存起来；峰值窗口里血条/弹道“本局创建 0”说明全部命中池。Player（Development Build）复测主线程 0.26~0.34 ms、GC P95 300~400 B。最后 EditMode 27 个、PlayMode 3 个测试全绿。

### 9.2 常见追问

**Q：为什么帧率没有提升？**  
A：60 帧封顶下 CPU 只占 0.43 ms，瓶颈不在 CPU。优化目标是降低每帧 GC 和主线程成本，给同屏单位留余量；如果解除封顶或换低端机，收益会体现在帧时间和 GC 停顿上。

**Q：UGUI 的 Text 不是会判重吗，为什么还有 GC？**  
A：`Text.text` setter 确实会比较字符串，但 `$"{a}/{b}"` 的插值在调用 setter 之前就已经分配了新字符串；另外每帧 `Resources.Load`、`Slider.value`、`Image.sprite` 等调用也有成本。事件化后这些调用只在值变化时发生。

**Q：事件驱动会不会漏刷新？**  
A：所有数值变化点都在 `BattleController` 内部：扣生命、扣费/回费、敌人加入/移除、选中变化；干员自身订阅 `HealthChanged` / `SpChanged`。HUD 初始化时统一订阅，`OnDestroy` 退订；死亡和撤退走 `AlliesChanged` 兜底，PlayMode 冒烟测试覆盖了选关、部署、撤退、面板开关。

**Q：对象池怎么避免引用泄漏？**  
A：`Release` 时解绑事件、清空 owner/target、失活后入池；取用时跳过已销毁的 null；切场景后 Unity 销毁池对象，静态栈里的 null 会被跳过。血条从单位根节点摘下再入池，不会跟着单位一起销毁。

**Q：为什么 Draw Calls 没降？**  
A：这次优化的是 CPU/UI 更新，不是渲染合批。Draw Calls 主要来自 60 个 Spine 单位和 60 套血条 Sprite；下一步才是图集、共享材质、静态合批。性能优化要按瓶颈排序，不能一次全上。

**Q：60 个敌人在编辑器 60 帧算压测吗？**  
A：算“可控的 CPU/GC 压测”，不等于真机帧率测试。它的价值是可复现、能定位到具体代码路径；真机数据要另做 Development Build + Profiler。

**Q：怎么保证前后可比？**  
A：同一个场景、同一相机、同一波次配置、同一组部署格、同样的自动技能逻辑、同样的 20 秒采样和峰值窗口；报告里记录 Unity 版本、平台、峰值同屏数、采样帧数；优化后跑两次确认波动在 5% 以内。

**Q：为什么血条池创建了 60 套，看起来没省？**  
A：峰值 60 同屏时 60 套是必须的；池化省的是“死亡后销毁 + 下一波重新实例化”。本场景 60 个敌人同时在场，死亡集中在后段，所以池的收益不在峰值窗口，而在换波和长时间运行。

**Q：你在这个项目里最想强调什么？**  
A：不是“我会写对象池”，而是“我能先量、再改、再用同一条件证明”。基线、指标、对照、回归测试是一套完整的方法，换到别的项目也能复用。

---

## 10. 附：关键文件索引

```text
场景
  Assets/_Project/Scenes/M1_Perf60.unity
配置
  Assets/StreamingAssets/Configs/map_M1_Perf60.json
  Assets/StreamingAssets/Configs/waves_M1_Perf60.json
  Assets/StreamingAssets/Configs/battle_M1_Perf60.json
采集与驱动
  Assets/_Project/Scripts/Battle/Runtime/Diagnostics/BattlePerformanceProbe.cs
  Assets/_Project/Scripts/Battle/Runtime/Diagnostics/PerformanceBenchmarkRunner.cs
  Assets/_Project/Scripts/Battle/Runtime/Diagnostics/ResourcesLoadTracker.cs
编辑器工具
  Assets/_Project/Scripts/Combat/Editor/PerfBenchmarkSceneBuilder.cs
  Assets/_Project/Scripts/Combat/Editor/ProfilerStatProbe.cs
测试
  Assets/_Project/Scripts/Tests/PlayMode/PerformanceBenchmarkTests.cs
数据
  Docs/PerfData/
截图
  Docs/PerfScreenshots/
```
