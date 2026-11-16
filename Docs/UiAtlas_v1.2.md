# UI 图集（P1-2）

> 目标：把 HUD/面板的小图标从「每张图一个材质 + 一次 draw call」收成一张 SpriteAtlas，用现有 `BattlePerformanceProbe` 的 `drawCalls` / `setPass` 指标做前后对比。

## 1. 收录规则

| 规则 | 值 | 原因 |
|---|---|---|
| 尺寸上限 | 长边 ≤ 600 | 更大的都是背景板（`ui_Bg` 5333×3000 等） |
| 排除立绘 | `UIHandDrawn/OperatorPortraits/**` | 立绘与裁切版同名，进同一图集会重名 |
| 类型 | `TextureImporterType.Sprite` | 非 Sprite 不参与打包 |
| 图集尺寸 | 2048（多页） | 显存减半；35 张 400×400 图标会拆成约 2 页 |
| 压缩 | BC7 / CompressedHQ | PC 端，带 alpha 的 UI 图 |

## 2. 生成图集

```text
Unity 菜单：Tools/PrismTactics/资源/生成 UI 图集
```

产物：`Assets/GameContent/UIHandDrawn/UIHandDrawnAtlas.spriteatlas`

放在 `UIHandDrawn` 目录里是刻意的：该目录已被 YooAsset 的 `UI` 收集器覆盖，图集地址自动是 `UIHandDrawn/UIHandDrawnAtlas`，不需要改收集配置。

脚本可重复执行：每次先清空旧收录项再重新收集，结果幂等。

## 3. 运行时加载

`UiAtlasService`（`Assets/_Project/Scripts/Battle/Runtime/Content/UiAtlasService.cs`）：

```text
Get(name, fallbackAddress)  → 图集取图，未命中回退单图地址
GetByPath(resourcePath)     → 用文件名查图集，原路径作为回退
```

已接入的调用点：

- `BattleHudBinder.LoadSprite`（暂停/倍速/费用/生命等 HUD 图标）
- `BattleHudBinder.LoadSpriteCached`（技能图标）
- `MetaFlowController.SetImage`（藏品图标）

`AssetService.ReleaseAll` 会顺带清空图集缓存，避免持有已释放的图集引用。

## 4. 怎么量

现有探针已经采集 `drawCalls` / `setPass` / `batches`，不需要新埋点：

```text
1. 菜单 `Tools → PrismTactics → 诊断 → 禁用 UI 图集（编辑器内）`，跑一次 60 敌压测
2. 再点一次恢复图集，跑同样场景
3. 对比 BattlePerformanceProbe 报告里的 drawCalls / setPass P95
```

也可以用 Frame Debugger 直接看 UI 批次：优化前每个图标一次 `Canvas.RenderOverlays`，优化后同一页图集合批。

## 5. 源图压缩警告

生成图集时 Console 会为每张收录图打印：

```text
Source Texture (...) of Sprite (xxx) is using compressed format.
To ensure no loss in source pixel details when packing to SpriteAtlas,
please use uncompressed format in TextureImporter.
```

这是**警告不是错误**：源图已经是 DXT 压缩，进图集后会被再压一次（DXT → BC7），细节有二次损失。

三种处理：

| 方案 | 做法 | 代价 |
|---|---|---|
| 保持现状（默认） | 忽略警告 | 图标最大显示 64~128 px，二次压缩看不出来 |
| 追求质量 | 菜单 `生成 UI 图集（源图无压缩）` | 这 35 张源图在 BattleRes 里变 RGBA32，包体变大 |
| 折中 | 只把 HUD 关键大图（生命/费用）改成无压缩 | 需要手工挑图 |

图集本身固定 BC7，所以最终运行时纹理大小由**图集**决定；`源图无压缩` 增大的是源资源与未被图集覆盖的引用。

## 6. 已知边界

- 立绘未进图集：仍是单图加载，选角/详情面板的图标数量少，不影响批次结论。
- 图集是 2048 多页（预计 2 页）：页与页之间不会合并，Draw Calls 的收益按“页数”而不是“1”来算。
- 图集资源会随 `BattleRes` 打进包，1.2 复测必须用重新构建的 Player/资源包，不能复用 1.1 的包。
