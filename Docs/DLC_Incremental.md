# PrismTactics 配置目录、DLC 粒度与增量下载

日期：2026-11-01  
前置：M3.1 / M3.2

---

## 1. 目录布局

```text
Assets/GameContent/Configs/
├── Core/       levels.json、battle.json、battle_1.json、battle_M1_Perf60.json
├── Map/        map.json、map_1.json、map_M1_Perf60.json
├── Waves/      waves.json、waves_1.json、waves_M1_Perf60.json
├── Units/      operators.json、enemies.json
└── Skills/     skills.json、relics.json
```

Collector 仍然只有一个 `Assets/GameContent/Configs`，`PackDirectory` 会按叶子目录出 bundle：

```text
assets_gamecontent_configs_core.bundle
assets_gamecontent_configs_map.bundle
assets_gamecontent_configs_waves.bundle
assets_gamecontent_configs_units.bundle
assets_gamecontent_configs_skills.bundle
```

地址仍是文件名（`levels`、`map_M1_Perf60`），所以 `BattleConfigLoader` 的 id 和 `levels.json` 的引用都不变。

## 2. 懒加载

启动只预加载 6 个全局配置：

```text
levels、relics、operators、skills、enemies、battle
```

地图、波次、每图战斗设置改为进关时按 address 加载：

```text
BattleConfigLoader.Load<MapDefinition>("map_1")
  → 缓存未命中
  → ContentService.TryLoadConfigText("map_1")
  → AssetHandle.WaitForAsyncComplete（文件很小，一次阻塞读取）
  → 写入缓存
```

这样几万张地图不会在 Boot 时全部进内存；只有当前关卡的地图/波次/战斗设置会加载。

## 3. DLC 目录策略

```text
Configs/Map/
├── Base/       初始地图
├── DLC_01/     活动 1（发布后不再修改）
├── DLC_02/     活动 2
└── DLC_03/     ...
```

- 每次活动新建 `DLC_0x`，不要往旧 DLC 里继续塞；否则旧 bundle 会越滚越大。
- 已发布目录不可变；要改旧图就发 `DLC_01_patch_01`（copy-on-write）。
- 大版本/常驻地图按固定容量分片（例如 100 张一片），避免单 bundle 过大。

## 4. 增量验证（本次实测）

1. 当前 `1.1.0` 产物：5 个配置 bundle。
2. 临时在 `Configs/Map/DLC_01` 放两张地图（`map_DLC01_A/B.json`），构建 `1.2.0`。
3. 对比两次产物：

```text
1.1.0 bundles: 5
1.2.0 bundles: 6
新增：444d83375cc03ecb7551a034522c41a6.bundle（DLC_01）
其余 5 个 bundle 文件名/hash 完全不变
```

说明新增两张地图只会新增一个 DLC bundle，不会重打或重下 Base/Map 的旧 bundle。验证后已删除临时 DLC 文件和 `1.2.0` 产物。

## 5. 版本号与下载量

- YooAsset 升版本号不等于全量下载：客户端比对清单里的 bundle hash，只下载缺失/变化的 bundle。
- 真正的下载量由 **bundle 粒度** 决定，而不是包数量。
- `release.json` 负责“选哪一版”，bundle 清单负责“下哪些文件”；回滚只改清单指针，旧 bundle 仍在缓存里可复用。

## 6. 后续

- HostPlayMode + 本地 HTTP 时，记录“新增两张地图”客户端实际下载字节数，作为最终验收。
- `levels.json` 如果增长到几千关，也要拆成 `levels_base.json` + `levels_dlc_xx.json`，避免每次新增都改总索引。
