# PrismTactics 第三阶段方案：HybridCLR 热更 + YooAsset 资源工作流

日期：2026-10-31  
工程：PrismTactics  
Unity：`2022.3.62f2c1`（中国版）

> 本方案按项目已有的 **YooAsset** 技术栈执行，替代早期的 Addressables 方案。  
> 目标平台：Windows Standalone；内容托管：本地 HTTP；热更范围：最小 DLL 闭环。

---

## 0. 结论

第三阶段拆成 5 个可独立验收的小阶段：

```text
M3.0 环境与兼容性验证（已完成大部分）
M3.1 YooAsset 资源迁移（配置 + UI + 单位 + 地图 + 战斗表现）
M3.2 内容版本清单 + 1.0/1.1 + 回退
M3.3 HybridCLR 最小代码热更
M3.4 构建 / 校验 / 发布流程
```

**不要一开始迁 `Combat`/`Battle` 进热更。** 先做最小 `PrismTactics.HotUpdate` 程序集，把链路跑通。

---

## 1. 已完成 / 现状

### 1.1 工程状态

- 分支：`master`
- 最新提交：以 `git log -1` 为准
- 测试：EditMode 29 / PlayMode 3
- 性能：60 敌人峰值窗口 GC 218.2 B/帧、主线程 0.289 ms

### 1.2 本轮已安装/设置

```text
com.code-philosophy.hybridclr  v9.0.3（git）
com.tuyoogame.yooasset         2.3.19（yoo2 分支）
com.unity.scriptablebuildpipeline 1.21.25
Windows Standalone             IL2CPP
ProjectSettings/HybridCLRSettings.asset  已按项目模板配置，hotUpdateAssemblies 暂为空
```

包已解析成功，现有 EditMode 29/29 通过。

### 1.3 资源现状

```text
Assets/Resources/                 162 个文件
Assets/StreamingAssets/Configs/   29 个文件
Resources.Load 调用              24 处
配置读取                          BattleConfigLoader + File.ReadAllText
```

---

## 2. 目标架构

### 2.1 启动链路

```text
AOT 启动器（PrismTactics.Launcher）
  -> 读取本地 content-manifest.json（选择 1.0 / 1.1 / previous）
  -> YooAssets.Initialize()
  -> 创建/初始化 BattleRes、HotUpdateRes 资源包
  -> 请求包版本 + 更新清单 + 下载差异
  -> 预加载配置 TextAsset
  -> 加载 HotUpdateRes 里的 HotUpdate.dll.bytes
  -> HybridCLR 加载热更 DLL
  -> 进入 Meta / 战斗
```

原则：

- AOT 只保留启动、YooAsset 初始化、热更加载器、必要接口。
- 场景上的 MonoBehaviour 仍然来自 AOT；热更代码通过接口/反射接入。
- 战斗内保持同步加载（启动时预加载配置和关键 Prefab）。
- 编辑器工具继续直接读写 `StreamingAssets`，运行时才走 YooAsset。

### 2.2 YooAsset 资源包规划

```text
BattleRes
├── Configs          levels/map/waves/operators/skills/enemies/battle/relics（TextAsset）
├── BattleUI         HUD / OperatorDetailPanel / OperatorActionPanel / Meta 面板
├── UnitCatalog      UnitPrefabCatalog + Spine 干员/敌人 Prefab
├── MapVisual        MapVisualConfig + 地面/高台/基地/出生点/背景 Prefab
├── CombatVFX        HealthBar / DamageNumber / Projectile
└── Sprites          UIHandDrawn 头像、技能图标、藏品图标、背景

HotUpdateRes
├── PrismTactics.HotUpdate.dll.bytes
└── AOT/mscorlib.dll.bytes（按需补元数据）
```

YooAsset 的 Collector 建议：

- 先全部用 **EditorSimulateMode** 在编辑器跑通；
- 再切 **HostPlayMode**（本地 HTTP / 远程）；
- 资源 Move 到 `Assets/GameContent/...` 时保留 `.meta`/GUID；
- 迁移完成后清空 `Assets/Resources/`；
- 地址用稳定规则，不要用运行时拼路径。

### 2.3 资源服务接口

```csharp
public interface IAssetService
{
    Task<T> LoadAssetAsync<T>(string location) where T : Object;
    Task<string> LoadTextAsync(string location);
    Task PreloadAsync(IEnumerable<string> locations);
}
```

实现：

- `YooAssetService`：包装 `ResourcePackage`，运行时。
- `EditorFileAssetService`：编辑器工具读文件，保持现有工作流。
- 业务代码只依赖接口，不直接调 `YooAssets`。

### 2.4 配置加载

```text
启动时把 levels/map/waves/operators/skills/enemies/battle/relics
一次性加载成字符串并反序列化到内存缓存
-> BattleConfigLoader.Load<T>() 读缓存
-> 编辑器工具仍读文件
```

---

## 3. 内容版本与回退

### 3.1 目录

```text
ServerData/StandaloneWindows64/
├── latest.json                 # 指向当前内容版本
├── 1.0.0/
│   ├── BattleRes/              # YooAsset 产物
│   ├── HotUpdateRes/
│   └── content-manifest.json
└── 1.1.0/
    ├── BattleRes/
    ├── HotUpdateRes/
    └── content-manifest.json
```

### 3.2 content-manifest.json

```json
{
  "appVersion": "1.0.0",
  "contentVersion": "1.1.0",
  "platform": "StandaloneWindows64",
  "battleResVersion": "1.1.0",
  "hotUpdateResVersion": "1.1.0",
  "previousVersion": "1.0.0",
  "minAppVersion": "1.0.0"
}
```

YooAsset 自己的 `PackageVersion` / `PackageManifest` 继续由 YooAsset 管理；这个文件负责“选哪个版本、回退到哪个版本”。

### 3.3 回退

1. **启动自动回退**：1.1 初始化/下载/清单失败 → 读 `previousVersion`，用 1.0 重试。
2. **手动回退**：启动参数 `--content-version 1.0.0` 或本地配置强制选择。
3. **服务器回退**：把 `latest.json` 改回 1.0.0，客户端下次启动走旧版本。
4. **缓存**：YooAsset 缓存目录要能在测试时清理；回退后不能命中错误缓存。

版本边界：

- 1.0/1.1 是内容版本，不是 Player 版本；
- 新内容依赖新代码时，用 `minAppVersion` 拦住旧 Player；
- 热更 DLL 也属于内容版本的一部分。

---

## 4. HybridCLR 最小热更

### 4.1 程序集划分

```text
AOT:
  PrismTactics.Launcher
    - IHotUpdateEntry
    - HotUpdateRuntime（持有入口、转发日志、隔离异常）
    - YooAsset 初始化 / 热更 DLL 加载

HotUpdate:
  PrismTactics.HotUpdate
    - HotUpdateEntry : IHotUpdateEntry
    - 返回版本号 / 改一段日志 / 改一个数值
```

`PrismTactics.HotUpdate.asmdef`：

- `autoReferenced: false`
- 引用 `PrismTactics.Launcher`
- 不引用 `PrismTactics.Battle` / `Combat`（先保持最小）

### 4.2 构建与加载

参考同类工程的 `HotUpdateBuilder` 流程：

1. `PrebuildCommand.GenerateAll()` 生成 HybridCLR 补丁数据；
2. 从 `Library/ScriptAssemblies/PrismTactics.HotUpdate.dll` 复制成
   `Assets/HotUpdateRes/PrismTactics.HotUpdate.dll.bytes`；
3. `AssetDatabase.ImportAsset(..., ForceSynchronousImport)`，校验导入内容；
4. 构建 YooAsset `HotUpdateRes`；
5. 旧 Player 从 `HotUpdateRes` 加载 DLL：
   ```csharp
   byte[] bytes = handle.GetAssetObject<TextAsset>().bytes;
   Assembly.Load(bytes);
   ```
6. 通过 `IHotUpdateEntry` 调用热更逻辑。

### 4.3 AOT 元数据

- 需要时把 `Library/HybridCLR/AssembliesPostIl2CppStrip/<Target>/mscorlib.dll` 复制成
  `AOT/mscorlib.dll.bytes`；
- 加载时 `RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet)`；
- 泛型实例化缺失会 `MissingMethodException`，先最小入口，逐步补 link.xml/AOT 引用。

### 4.4 验收

- 打一次 Player，记录热更版本 v1；
- 改热更代码一行；
- 只构建 DLL + `HotUpdateRes`，不重新打 Player；
- 旧 Player 加载新内容，行为变化；
- 热更失败时保留旧 DLL，不崩主流程。

---

## 5. 构建 / 校验 / 发布

建议菜单：

```text
Tools/PrismTactics/第三阶段/
  1. 校验资源引用
  2. 构建 YooAsset 内容包
  3. 同步热更 DLL
  4. 构建 Player
  5. 生成内容清单
  6. 一键发布（内容版本号）
```

校验：

- 运行时无 `Resources.Load`（编辑器工具白名单）；
- 运行时无 `StreamingAssets` 读取；
- YooAsset Collector 无重复地址；
- 配置 JSON 全部在 `BattleRes/Configs`；
- 热更 DLL 在 `HotUpdateRes`；
- `latest.json` / 1.0 / 1.1 目录文件存在；
- 旧 32 个测试 + 新增测试全绿。

本地 HTTP：

```powershell
cd ServerData\StandaloneWindows64
python -m http.server 8080
```

参考同类工程的 `RemoteServices` / HostPlayMode 参数。

---

## 6. 工作分解

### M3.0 环境与兼容性（大部分已完成）

已完成：

- 安装 HybridCLR v9.0.3
- 安装 YooAsset 2.3.19（yoo2）
- 安装 SBP 1.21.25
- Windows Standalone 切 IL2CPP
- 复制 `HybridCLRSettings.asset`
- 现有 EditMode 29/29 通过

剩余：

- 打开 YooAsset Collector 创建 `BattleRes` / `HotUpdateRes`
- 创建 `PrismTactics.Launcher` / `PrismTactics.HotUpdate` 程序集
- 打一个 IL2CPP 空包，确认 Windows Build Support / IL2CPP 工具链正常

### M3.1 YooAsset 资源迁移（2~3 天）

- 建 Collector / 地址规则
- 资源 Move 出 `Resources`
- `IAssetService` + 启动预加载
- 改 24 处 `Resources.Load`
- 改 `BattleConfigLoader`
- 清空 `Resources`
- 跑全流程 + 测试

### M3.2 版本清单 + 1.0/1.1 + 回退（1~2 天）

- `content-manifest.json`
- 1.0/1.1 两套 YooAsset 产物
- 启动版本选择 / 自动回退 / 手动回退
- 本地 HTTP 验证

### M3.3 HybridCLR 最小热更（2~3 天）

- AOT Launcher + HotUpdate 程序集
- DLL 同步 + YooAsset 包
- 旧 Player 热更验证
- 限制说明

### M3.4 构建 / 校验 / 发布（1~2 天）

- 菜单/命令行
- 校验
- 清单
- 文档

---

## 7. 关键决策（已确认）

- 平台：Windows Standalone
- 托管：本地 HTTP 测试
- 热更：最小 DLL 闭环
- IL2CPP：接受
- 迁移后彻底删除 `Resources`
- 回退：自动 + 手动都做
- 资源框架：YooAsset（不使用 Addressables）
- 同类工程已验证 HybridCLR/YooAsset 流程

---

## 8. 风险

| 风险 | 规避 |
|---|---|
| HybridCLR 泛型/AOT 元数据缺失 | 先最小入口；补 link.xml/AOT 引用；日志隔离异常 |
| YooAsset 迁移导致启动时序变化 | 先 EditorSimulateMode，配置预加载，战斗内同步 |
| 资源 Move 断 GUID | 用 Unity Move，保留 .meta，迁移后全量校验 |
| `Resources` 双份 | 迁移后清空，静态检查 |
| 版本/回退缓存污染 | 每次测试清 YooAsset 缓存，1.0/1.1 独立目录 |
| IL2CPP 构建慢 | 区分内容构建与 Player 构建，增量 |
| 包版本/中国版兼容 | 已按同版本安装，先打空包验证 |

---

## 9. 本周建议

```text
第 1 步：IL2CPP 空包 + YooAsset Collector 建包（M3.0）
第 2 步：只迁 Configs，EditorSimulateMode 跑通（M3.1 第一半）
第 3 步：迁 BattleUI / CombatVFX，HostPlayMode 本地 HTTP（M3.1 第二半）
第 4 步：1.0/1.1 + 回退（M3.2）
第 5 步：最小 HybridCLR DLL（M3.3）
第 6 步：一键构建/校验（M3.4）
```

## 10. 明确不做

- 真实 CDN / 灰度 / A/B；
- 代码加密、防破解；
- 全量战斗代码热更迁移（先最小闭环）；
- 图集/合批（渲染阶段）；
- Android/iOS。
