/****************************************************
    文件：M1BattleBootstrap.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：M1/M3 启动器：加载配置、搭建地图、波次、部署与 HUD。
*****************************************************/

using PrismTactics.Combat;
using PrismTactics.Launcher;
using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class M1BattleBootstrap : MonoBehaviour
    {
        #region 序列化字段
        [SerializeField] private bool _configureCamera = true;// 是否在运行时配置相机（清屏色等）；相机位置/旋转/FOV 仍以场景设置为准
        [SerializeField] private string _mapId = "";// 地图 ID；为空时使用加载到的 MapDefinition.mapId
        [SerializeField] private bool _autoBuild = true;// 进入 Play 后是否自动搭建战场
        #endregion

        #region 私有状态字段
        private BattleHotUpdateVisualService _hotUpdateVisuals;// 当前热更表现服务实例，负责热更相关的视觉/逻辑清理
        // 运行时动态创建的 ScriptableObject 资产（干员/技能/攻击配置），
        // 需要在 OnDestroy 中手动 Destroy，避免泄漏。
        // 注意：这里的 Object 是 UnityEngine.Object，不是 System.Object。
        private readonly List<Object> _runtimeAssets = new List<Object>();
        #endregion

        #region Unity 生命周期
        private void Start()
        {
            if (_autoBuild)
            {
                Build();
            }
        }

        /// <summary>
        /// Unity 生命周期：对象销毁时清理运行时创建的资产、热更服务和全局单例引用，
        /// 避免退出战斗后残留引用或泄漏 ScriptableObject。
        /// </summary>
        private void OnDestroy()
        {
            // 释放运行时创建的 ScriptableObject（干员/技能/攻击配置）
            for (int i = 0; i < _runtimeAssets.Count; i++)
            {
                if (_runtimeAssets[i] != null)
                {
                    Destroy(_runtimeAssets[i]);
                }
            }

            // 清理热更表现服务
            if (_hotUpdateVisuals != null)
            {
                _hotUpdateVisuals.ClearAll();
                _hotUpdateVisuals = null;
            }

            _runtimeAssets.Clear();

            // 清空全局单例与热更 Tick，避免下次进入战斗时复用旧状态
            HotUpdateVisualService.Instance = null;
            HotUpdateTickService.Clear();
        }

        #endregion

        #region 公开接口
        /// <summary>
        /// 外部（例如关卡选择流程）在 Build 前设置要加载的地图 ID。
        /// </summary>
        public void SetMapId(string mapId)
        {
            _mapId = mapId;
        }

        /// <summary>
        /// 搭建 M1/M3 战场：加载配置、创建地图/网格/战斗世界、装配干员与波次、
        /// 初始化相机灯光、战斗控制器、部署控制器和 HUD。
        /// 仅允许在 Play Mode 且内容已加载时执行。
        /// </summary>
        [ContextMenu("重新生成 M1 战场")]
        public void Build()
        {
            // 1. 前置校验：必须处于 Play Mode，且内容服务已加载完成
            if (!Application.isPlaying)
            {
                Debug.LogWarning("M1BattleBootstrap.Build 只能在 Play Mode 下运行。");
                return;
            }

            if (!ContentService.IsLoaded)
            {
                Debug.LogError("[Content] 内容尚未加载，请从 Bootstrap 场景启动。"
                               + "菜单：Tools/PrismTactics/第三阶段/打开 Bootstrap 场景");
                return;
            }
            // 2. 重置热更表现服务与 Tick，确保每次 Build 都是干净状态
            if (_hotUpdateVisuals != null)
            {
                _hotUpdateVisuals.ClearAll();
            }
            HotUpdateTickService.Clear();
            _hotUpdateVisuals = new BattleHotUpdateVisualService();
            HotUpdateVisualService.Instance = _hotUpdateVisuals;

            // 3. 加载地图定义；失败时用内置兜底地图，保证流程不中断
            MapDefinition mapDefinition = BattleConfigLoader.LoadMap(_mapId);
            if (mapDefinition == null)
            {
                mapDefinition = CreateFallbackMap();
            }

            // 实际生效的地图 ID：外部未指定时以地图定义里的 mapId 为准
            string activeMapId = string.IsNullOrEmpty(_mapId) ? mapDefinition.mapId : _mapId;

            // 4. 创建地图与网格（战斗逻辑的坐标空间）
            BattleMap map = new BattleMap(mapDefinition);
            BattleGrid grid = new BattleGrid(map);
            // 格子片对象池：范围块/高亮/后续区域贴片共用，随战斗场景销毁
            BattleTilePool.Create(transform);

            // 5. 查找或创建 CombatWorld（战斗世界容器），并挂上网格空间
            CombatWorld world = FindObjectOfType<CombatWorld>();
            if (world == null)
            {
                world = new GameObject("Combat World").AddComponent<CombatWorld>();
            }

            world.Space = grid;

            // 6. 加载各类配置库；各自失败时用兜底数据，保证 Build 可完成
            OperatorLibrary operatorLibrary = BattleConfigLoader.Load<OperatorLibrary>(BattleConfigIds.Operators);
            if (operatorLibrary == null)
            {
                operatorLibrary = CreateFallbackOperators();
            }

            EnemyLibrary enemyLibrary = BattleConfigLoader.Load<EnemyLibrary>(BattleConfigIds.Enemies);
            if (enemyLibrary == null)
            {
                enemyLibrary = CreateFallbackEnemies();
            }

            WaveLibrary waveLibrary = BattleConfigLoader.LoadWaves(activeMapId);
            if (waveLibrary == null)
            {
                waveLibrary = CreateFallbackWaves();
            }

            BattleSettings settings = BattleConfigLoader.LoadBattle(activeMapId);
            if (settings == null)
            {
                settings = new BattleSettings();
            }
            // 叠加热更带来的初始费用加成
            settings.startingCost += HotUpdateRuntime.StartingCostBonus;

            SkillLibrary skillLibrary = BattleConfigLoader.Load<SkillLibrary>(BattleConfigIds.Skills);
            if (skillLibrary == null)
            {
                skillLibrary = new SkillLibrary();
            }

            RelicLibrary relicLibrary = BattleConfigLoader.Load<RelicLibrary>("relics");
            RelicBuff relicBuff = RelicBuff.FromSession(relicLibrary);

            // 道具库：缺失时给空库，进战不显示道具卡
            ItemLibrary itemLibrary = BattleConfigLoader.Load<ItemLibrary>(BattleConfigIds.Items);
            if (itemLibrary == null)
            {
                itemLibrary = new ItemLibrary();
            }

            // 7. 把定义配置转换为运行时配置，并逐个应用遗物 buff
            List<OperatorConfig> operators = BuildOperatorConfigs(operatorLibrary, skillLibrary);
            for (int i = 0; i < operators.Count; i++)
            {
                relicBuff.Apply(operators[i]);
            }
            // 8. 相机/灯光与地图视图
            EnsureCameraAndLight(map);
            BattleMapView.Build(map, transform);

            // 9. 创建战斗控制器并注入世界、地图、网格、配置
            BattleController controller = new GameObject("Battle Controller").AddComponent<BattleController>();
            controller.Initialize(world, map, grid, settings, operators, enemyLibrary, waveLibrary, itemLibrary);

            // 10. 预热战斗表现池：把出怪/受击高峰时的 Instantiate 成本提前到进战初始化。。
            UnitStatusBarFactory.Prewarm(20);
            ProjectileManager.Prewarm(8);
            DamageNumberManager.Ensure().Warmup(24);

            // 11. 部署控制器（拖拽部署、格子选择等）
            BattleDeploymentController deployment = controller.gameObject.AddComponent<BattleDeploymentController>();
            deployment.Initialize(controller, grid, Camera.main);

            // 12. HUD：优先绑定场景中已有的 HUD，否则运行时创建一个
            BattleHudBinder binder = TryBindHud(controller, deployment);
            if (binder == null)
            {
                BattleHud hud = gameObject.AddComponent<BattleHud>();
                hud.Initialize(controller, deployment);
            }
            else
            {
                // 存在 HUD Binder 时，额外挂载拖拽控制器以支持 UI 拖拽部署
                BattleDeploymentDragController drag = controller.gameObject.AddComponent<BattleDeploymentDragController>();
                drag.Initialize(controller, grid, Camera.main, binder, deployment);
            }
            // 13. 调试工具：编辑器/开发包可用，F10 或右上角按钮直接结束战斗，方便验证掉落与结算
            if (Application.isEditor || Debug.isDebugBuild)
            {
                BattleDebugTools.Create(controller);
            }

            // 14. 输出战斗开始日志，便于调试
            Debug.Log("[M1] 战斗开始：地图 " + map.MapId + "，生命 " + controller.Life + "，费用 " + controller.Cost);
        }
        #endregion

        #region HUD 绑定
        /// <summary>
        /// 尝试查找场景中的 HUD 并挂载 BattleHudBinder。
        /// 找不到 HUD 对象时返回 null，由调用方决定是否回退到运行时创建的 BattleHud。
        /// </summary>
        private BattleHudBinder TryBindHud(BattleController controller, BattleDeploymentController deployment)
        {
            // 场景中约定名为 "HUD" 的对象作为 HUD 根
            GameObject hudObject = GameObject.Find("HUD");
            if (hudObject == null)
            {
                return null;
            }
            // 若 HUD 是某个父对象的子节点，则在父对象上挂 Binder，避免破坏 HUD 自身层级
            Transform root = hudObject.transform.parent != null
                ? hudObject.transform.parent
                : hudObject.transform;

            // 复用已有 Binder，没有则新增
            BattleHudBinder binder = root.GetComponent<BattleHudBinder>();
            if (binder == null)
            {
                binder = root.gameObject.AddComponent<BattleHudBinder>();
            }

            binder.Initialize(controller, deployment);
            return binder;
        }
        #endregion

        #region 干员/技能/攻击运行时配置构建
        /// <summary>
        /// 把 OperatorLibrary（数据定义）转换为运行时使用的 OperatorConfig 列表：
        /// 1. 先把 SkillLibrary 转成 id → SkillConfig 的映射；
        /// 2. 逐个干员创建 OperatorConfig，复制数值字段；
        /// 3. 按 skillIds 关联技能，并按会话中选择的技能调整顺序；
        /// 4. 所有运行时 ScriptableObject 记入 _runtimeAssets 统一销毁。
        /// </summary>
        private List<OperatorConfig> BuildOperatorConfigs(OperatorLibrary library, SkillLibrary skillLibrary)
        {
            var result = new List<OperatorConfig>();
            if (library == null || library.operators == null)
            {
                return result;
            }
            // 先构建 技能ID → SkillConfig 映射，避免后续每个干员重复创建技能配置
            var skillMap = new Dictionary<string, SkillConfig>();
            if (skillLibrary != null && skillLibrary.skills != null)
            {
                for (int i = 0; i < skillLibrary.skills.Count; i++)
                {
                    SkillDefinition definition = skillLibrary.skills[i];
                    if (definition == null || string.IsNullOrEmpty(definition.id))
                    {
                        continue;
                    }

                    skillMap[definition.id] = CreateSkillConfig(definition);
                }
            }
            // 逐个干员定义创建运行时 OperatorConfig
            for (int i = 0; i < library.operators.Count; i++)
            {
                OperatorDefinition definition = library.operators[i];
                if (definition == null)
                {
                    continue;
                }

                // 创建运行时 ScriptableObject，标记 DontSave 避免被场景序列化
                OperatorConfig config = ScriptableObject.CreateInstance<OperatorConfig>();
                config.name = "Runtime Operator " + definition.id;
                config.hideFlags = HideFlags.DontSave;
                config.operatorId = definition.id;
                config.displayName = definition.displayName;
                config.maxHealth = definition.maxHealth;
                config.attack = definition.attack;
                config.defense = definition.defense;
                config.magicResist = definition.magicResist;
                config.attackIntervalTicks = definition.attackIntervalTicks;
                config.windupTicks = definition.windupTicks;
                config.attackHitNormalizedTime = definition.attackHitNormalizedTime;
                config.useProjectile = definition.useProjectile;
                config.projectileSpeed = definition.projectileSpeed;
                config.blockCount = definition.blockCount;
                config.deployCost = definition.deployCost;
                config.canDeployOnHighGround = definition.canDeployOnHighGround;
                config.requiresHighGround = definition.requiresHighGround;
                config.isHealer = definition.isHealer;
                config.targetSelector = (TargetSelectorType)definition.targetSelector;
                config.rangeCells = ConvertRange(definition.rangeCells);
                config.normalAttack = CreateAttack(definition);
                config.debugColor = new Color(definition.colorR, definition.colorG, definition.colorB);
                config.attackRange = 3f;// 表现层攻击范围（世界单位），与 rangeCells 逻辑范围相互独立

                // 按定义中的 skillIds 关联技能配置
                var skills = new List<SkillConfig>();
                if (definition.skillIds != null)
                {
                    for (int s = 0; s < definition.skillIds.Count; s++)
                    {
                        if (skillMap.TryGetValue(definition.skillIds[s], out SkillConfig skill))
                        {
                            skills.Add(skill);
                        }
                    }
                }
                // 会话中选择了非首位技能时，把它移动到首位（索引 0 视为默认技能，无需调整）
                int selectedSkill = RunSession.GetSelectedSkillIndex(definition.id);
                if (selectedSkill > 0 && selectedSkill < skills.Count)
                {
                    SkillConfig selected = skills[selectedSkill];
                    skills.RemoveAt(selectedSkill);
                    skills.Insert(0, selected);
                }

                config.skills = skills.ToArray();
                result.Add(config);
                _runtimeAssets.Add(config);
            }

            return result;
        }
        /// <summary>
        /// 把 SkillDefinition（数据定义）转换为运行时 SkillConfig，并登记到 _runtimeAssets。
        /// 枚举字段通过显式强转保持与数据定义中的整型值对齐。
        /// </summary>
        private SkillConfig CreateSkillConfig(SkillDefinition definition)
        {
            SkillConfig config = ScriptableObject.CreateInstance<SkillConfig>();
            config.name = "Runtime Skill " + definition.id;
            config.hideFlags = HideFlags.DontSave;
            config.skillId = definition.id;
            config.effectKey = definition.effectKey;
            config.displayName = definition.displayName;
            config.description = definition.description;
            config.activationType = (SkillActivationType)definition.activationType;
            config.spRecoveryType = (SpRecoveryType)definition.spRecoveryType;
            config.spCost = definition.spCost;
            config.initialSp = definition.initialSp;
            config.spPerSecond = definition.spPerSecond;
            config.effectType = (SkillEffectType)definition.effectType;
            config.durationTicks = definition.durationTicks;
            config.damageType = (DamageType)definition.damageType;
            config.damageMultiplier = definition.damageMultiplier;
            config.hitCount = Mathf.Max(1, definition.hitCount);// 至少 1 段命中
            config.extraHits = Mathf.Max(0, definition.extraHits);// 额外命中数不能为负
            config.attackPercent = definition.attackPercent;
            config.attackSpeedPercent = definition.attackSpeedPercent;
            config.healPercent = definition.healPercent;
            config.attacksAllInRange = definition.attacksAllInRange;
            config.requiresEnemy = definition.requiresEnemy;
            config.attackIntervalReducePercent = definition.attackIntervalReducePercent;
            config.multiTargetCount = definition.multiTargetCount;
            config.projectileTint = definition.projectileTint;
            config.rangeCells = definition.rangeCells != null && definition.rangeCells.Count > 0
                ? ConvertRange(definition.rangeCells)
                : new Vector2Int[0];
            _runtimeAssets.Add(config);
            return config;
        }
        /// <summary>
        /// 为干员创建运行时普通攻击数据（AttackData），并登记到 _runtimeAssets。
        /// 当前统一为单体、单目标形态，具体数值来自干员定义。
        /// </summary>
        private AttackData CreateAttack(OperatorDefinition definition)
        {
            AttackData attack = ScriptableObject.CreateInstance<AttackData>();
            attack.name = "Runtime Attack " + definition.id;
            attack.hideFlags = HideFlags.DontSave;
            attack.damageType = (DamageType)definition.damageType;
            attack.baseDamage = definition.baseDamage;
            attack.attackScale = definition.attackScale;
            attack.damageShape = DamageShape.Single;
            attack.maxTargets = 1;
            _runtimeAssets.Add(attack);
            return attack;
        }
        /// <summary>
        /// 把配置中的 GridPoint 列表转换为运行时使用的 Vector2Int 数组。
        /// 空列表时回退为包含原点 (0,0) 的单元素数组，保证至少覆盖自身格。
        /// </summary>
        private static Vector2Int[] ConvertRange(List<GridPoint> points)
        {
            if (points == null || points.Count == 0)
            {
                return new[] { Vector2Int.zero };
            }

            var result = new Vector2Int[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                result[i] = points[i].ToVector2Int();
            }

            return result;
        }
        #endregion

        #region 相机与灯光
        /// <summary>
        /// 确保场景中存在主相机与平行光；缺失时运行时创建。
        /// 相机位置/旋转/FOV 使用场景中的设置，运行时只按需覆盖清屏相关属性。
        /// </summary>
        private void EnsureCameraAndLight(BattleMap map)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraGo = new GameObject("Main Camera");
                cameraGo.tag = "MainCamera";
                camera = cameraGo.AddComponent<Camera>();
                cameraGo.AddComponent<AudioListener>();
            }

            if (_configureCamera)
            {
                // 相机位置/旋转/FOV 使用场景中的设置，运行时不再覆盖。
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            }
            // 场景无光源时补一盏平行光，避免渲染全黑
            if (FindObjectOfType<Light>() == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }
        #endregion

        #region 兜底数据构建
        /// <summary>
        /// 创建兜底地图：12x6 地面，内置若干高台与三条出怪路线。
        /// 仅在配置加载失败时使用，保证战场仍可运行。
        /// </summary>
        private static MapDefinition CreateFallbackMap()
        {
            var definition = new MapDefinition
            {
                mapId = "M1_Fallback",
                width = 12,
                height = 6,
                baseCell = new GridPoint(0, 3)// 基地固定在左侧中间格
            };
            // 逐格生成地形：默认地面可部署，指定坐标设为高台
            for (int x = 0; x < definition.width; x++)
            {
                for (int y = 0; y < definition.height; y++)
                {
                    var cell = new MapCellDefinition
                    {
                        x = x,
                        y = y,
                        terrain = TerrainType.Ground,
                        deployable = true
                    };

                    bool highGround =
                        (x == 2 && (y == 1 || y == 2 || y == 4 || y == 5)) ||
                        (x == 5 && (y == 0 || y == 1 || y == 4 || y == 5)) ||
                        (x == 8 && (y == 2 || y == 3));

                    if (highGround)
                    {
                        cell.terrain = TerrainType.HighGround;
                    }

                    definition.cells.Add(cell);
                }
            }
            // 三条出怪路线：上/中/下
            definition.spawnPoints.Add(new SpawnPointDefinition { x = 11, y = 1, routeId = "route_top" });
            definition.spawnPoints.Add(new SpawnPointDefinition { x = 11, y = 3, routeId = "route_mid" });
            definition.spawnPoints.Add(new SpawnPointDefinition { x = 11, y = 5, routeId = "route_bottom" });
            return definition;
        }
        /// <summary>
        /// 创建兜底干员库：仅包含一个基础近战干员 "guard"。
        /// </summary>
        private static OperatorLibrary CreateFallbackOperators()
        {
            var library = new OperatorLibrary();
            var guard = new OperatorDefinition
            {
                id = "guard",
                displayName = "禁卫",
                maxHealth = 140f,
                attack = 18f,
                defense = 5f,
                magicResist = 0f,
                attackIntervalTicks = 30,
                windupTicks = 9,
                blockCount = 2,
                deployCost = 5,
                canDeployOnHighGround = true,
                targetSelector = 0,
                damageType = 0,
                baseDamage = 0f,
                attackScale = 1f
            };
            guard.rangeCells.Add(new GridPoint(0, 0));
            guard.rangeCells.Add(new GridPoint(1, 0));
            library.operators.Add(guard);
            return library;
        }
        /// <summary>
        /// 创建兜底波次库：三波，每波各走一条路线，每次出 1 个敌人。
        /// </summary>
        private static EnemyLibrary CreateFallbackEnemies()
        {
            var library = new EnemyLibrary();
            library.enemies.Add(new EnemyDefinition
            {
                id = 1,
                displayName = "普通敌人",
                maxHealth = 60f,
                defense = 0f,
                moveSpeed = 1.5f,
                blockWeight = 1,
                lifeDamage = 1
            });
            return library;
        }
        // <summary>
        /// 创建兜底波次库：三波，每波各走一条路线，每次出 1 个敌人。
        /// </summary>
        private static WaveLibrary CreateFallbackWaves()
        {
            var library = new WaveLibrary();
            string[] routes = { "route_top", "route_mid", "route_bottom" };
            for (int i = 0; i < 3; i++)
            {
                var wave = new WaveDefinition
                {
                    waveNumber = i + 1,
                    delaySeconds = 1f
                };
                wave.spawns.Add(new WaveSpawnDefinition
                {
                    enemyId = 1,
                    routeId = routes[i],
                    count = 1,
                    intervalSeconds = 0f
                });
                library.waves.Add(wave);
            }

            return library;
        }
        #endregion
    }
}
