/****************************************************
    文件：BattleController.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：战斗总控：生命、费用、部署、波次、敌人回收与胜负判定。
*****************************************************/

using System;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class BattleController : MonoBehaviour
    {
        #region 私有字段
        private readonly List<EnemyUnit> _enemies = new List<EnemyUnit>();// 当前存活敌人列表；EnemyFactory 生成时加入，死亡/漏怪时移除
        private readonly List<CombatEntity> _allies = new List<CombatEntity>();// 当前已部署干员（友方 CombatEntity）列表
        private readonly List<UnityEngine.Object> _runtimeAssets = new List<UnityEngine.Object>();//运行时动态创建的 Unity 对象（如兜底材质），OnDestroy 时统一销毁
        private readonly List<CombatEntity> _enemyBuffer = new List<CombatEntity>();// 临时缓冲：查询指定格子上的敌人时复用，避免每次部署都新建 List

        private BattleSettings _settings;// 本局战斗设置（初始生命、初始费用、费用恢复速度、费用上限等）
        // 当前地图与网格（战斗坐标空间）
        private BattleMap _map;
        private BattleGrid _grid;

        private UnitPrefabCatalog _unitCatalog;// 干员/敌人预制体目录
        // 敌人生成工厂与波次管理器
        private EnemyFactory _enemyFactory;
        private WaveManager _waveManager;

        private float _costAccumulator;// 费用恢复的小数累积器；累计满 1 才增加费用
        // 当前生命、当前费用
        private int _life;
        private int _cost;

        private int _resolvedEnemies; // 已解决的敌人数（击杀 + 漏怪），用于计算剩余敌人
        // 本局道具剩余次数（itemId → 次数），进战时按配置初始化
        private readonly Dictionary<string, int> _itemCharges = new Dictionary<string, int>();
        #endregion

        #region 公开属性与事件
        public CombatWorld World { get; private set; }// 战斗世界（Tick 驱动源）
        // 网格与地图
        public BattleGrid Grid => _grid;
        public BattleMap Map => _map;
        // 敌人/干员只读列表
        public IReadOnlyList<EnemyUnit> Enemies => _enemies;
        public IReadOnlyList<CombatEntity> Allies => _allies;
        // 本局可用干员配置
        public IReadOnlyList<OperatorConfig> Operators { get; private set; }
        // 本局可用道具配置
        public IReadOnlyList<ItemDefinition> Items { get; private set; }
        // 道具次数变化（使用后刷新卡牌）
        public event Action ItemsChanged;
        // 干员列表变化（部署/撤退/死亡）
        public event Action AlliesChanged;
        // UI 事件化：只在数值真正变化时通知，避免 HUD 每帧轮询和字符串拼接。
        public event Action LifeChanged;
        public event Action CostChanged;
        public event Action EnemiesChanged;
        public event Action<CombatEntity> SelectionChanged;
        // 当前选中的干员
        public CombatEntity SelectedAlly { get; private set; }
        // 当前生命、最大生命
        public int Life => _life;
        public int MaxLife => _settings != null ? _settings.initialLife : 0;
        public int Cost => _cost;// 当前费用
        // 战斗状态：Running / Victory / Defeat
        public BattleState State { get; private set; } = BattleState.Running;
        public WaveManager Waves => _waveManager;// 波次管理器

        /// <summary>
        /// UI 只读数据：本局总敌人数。
        /// </summary>
        public int TotalEnemies => _waveManager != null ? _waveManager.TotalEnemyCount : 0;

        /// <summary>
        /// UI 只读数据：剩余敌人 = 总敌人数 - 已击杀/已漏怪。
        /// </summary>
        public int RemainingEnemies => Mathf.Max(0, TotalEnemies - _resolvedEnemies);
        // 已解决、存活、波次计数（UI 只读）
        public int ResolvedEnemies => _resolvedEnemies;
        public int LiveEnemies => _enemies.Count;
        public int CurrentWave => _waveManager != null ? _waveManager.CurrentWave : 0;
        public int TotalWaves => _waveManager != null ? _waveManager.TotalWaves : 0;
        #endregion

        #region 初始化
        /// <summary>
        /// 由 M1BattleBootstrap 调用：注入地图/网格/设置/干员/敌人/波次，
        /// 创建敌人工厂与波次管理器，加载预制体目录，并订阅世界 Tick。
        /// </summary>
        public void Initialize(
            CombatWorld world,
            BattleMap map,
            BattleGrid grid,
            BattleSettings settings,
            List<OperatorConfig> operators,
            EnemyLibrary enemyLibrary,
            WaveLibrary waveLibrary,
            ItemLibrary itemLibrary = null)
        {
            World = world;
            _map = map;
            _grid = grid;
            _settings = settings;
            Operators = operators;
            Items = itemLibrary != null && itemLibrary.items != null
                ? itemLibrary.items
                : new List<ItemDefinition>();
            for (int i = 0; i < Items.Count; i++)
            {
                ItemDefinition item = Items[i];
                if (item != null && !string.IsNullOrEmpty(item.id))
                {
                    _itemCharges[item.id] = Mathf.Max(0, item.charges);
                }
            }
            // 生命/费用从设置读取；设置缺失时给默认值，保证流程不崩
            _life = settings != null ? settings.initialLife : 3;
            _cost = settings != null ? settings.startingCost : 10;
            // 从资源服务加载干员/敌人预制体目录
            _unitCatalog = AssetService.Load<UnitPrefabCatalog>("UnitPrefabCatalog");
            // 战斗世界使用网格作为坐标空间
            world.Space = grid;
            // 创建敌人工厂与波次管理器，并预热敌人对象池
            _enemyFactory = new EnemyFactory(this, grid, enemyLibrary);
            _waveManager = new WaveManager(waveLibrary, map, _enemyFactory, this);
            _enemyFactory.Prewarm(waveLibrary);

            // 订阅世界 Tick，作为战斗主循环的驱动
            world.Ticked += OnTick;
        }
        #endregion

        #region 敌人管理
        /// <summary>
        /// 由 EnemyFactory 在生成敌人时调用：加入敌人列表并通知 UI。
        /// 重复添加会被忽略。
        /// </summary>
        public void AddEnemy(EnemyUnit enemy)
        {
            if (enemy != null && !_enemies.Contains(enemy))
            {
                _enemies.Add(enemy);
                EnemiesChanged?.Invoke();
            }
        }
        /// <summary>
        /// 由敌人自身在死亡时调用：计入已解决数并从列表/场景中移除。
        /// </summary>
        public void OnEnemyRemoved(EnemyUnit enemy)
        {
            if (enemy == null || !_enemies.Contains(enemy))
            {
                return;
            }

            _resolvedEnemies++;
            RemoveEnemy(enemy, true);
        }
        /// <summary>
        /// 由敌人抵达基地时调用：扣除生命、计入已解决数、移除敌人，并检查失败。
        /// 战斗已结束或敌人不在列表中时忽略。
        /// </summary>
        public void OnEnemyLeaked(EnemyUnit enemy)
        {
            if (enemy == null || State != BattleState.Running || !_enemies.Contains(enemy))
            {
                return;
            }
            // 漏怪伤害由敌人配置决定，至少扣 1 点生命
            int damage = enemy.Config != null ? enemy.Config.lifeDamage : 1;
            _life = Mathf.Max(0, _life - damage);
            _resolvedEnemies++;
            LifeChanged?.Invoke();
            RemoveEnemy(enemy, true);

            if (_life <= 0)
            {
                State = BattleState.Defeat;
            }
        }
        #endregion

        #region 部署与撤退

        /// <summary>
        /// UI 只读查询：当前费用和战斗状态是否允许部署这个干员。
        /// 已部署的干员不允许重复部署。
        /// </summary>
        public bool CanDeploy(OperatorConfig operatorConfig)
        {
            return State == BattleState.Running
                && operatorConfig != null
                && _cost >= operatorConfig.deployCost
                && !IsOperatorDeployed(operatorConfig);
        }
        /// <summary>
        /// 判断指定干员配置是否已经部署在场上。
        /// </summary>
        public bool IsOperatorDeployed(OperatorConfig operatorConfig)
        {
            return operatorConfig != null && IsOperatorDeployed(operatorConfig.operatorId);
        }
        /// <summary>
        /// 按 operatorId 判断是否已部署；遍历已部署干员。
        /// </summary>
        public bool IsOperatorDeployed(string operatorId)
        {
            if (string.IsNullOrEmpty(operatorId))
            {
                return false;
            }

            for (int i = 0; i < _allies.Count; i++)
            {
                // 模式匹配取 Config，避免手动强转
                if (_allies[i] != null
                    && _allies[i].Config is OperatorConfig config
                    && config.operatorId == operatorId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// UI 只读查询：指定格子是否可部署这个干员。
        /// 除费用/状态外，还要检查格子是否可部署（高台/地面限制）。
        /// </summary>
        public bool CanDeployAt(OperatorConfig operatorConfig, Vector2Int cell)
        {
            return CanDeploy(operatorConfig)
                && _grid != null
                && _grid.IsDeployable(
                    cell,
                    operatorConfig.canDeployOnHighGround,
                    operatorConfig.requiresHighGround);
        }
        /// <summary>
        /// 尝试在指定格子部署干员：
        /// 1. 校验可部署性；
        /// 2. 创建逻辑根节点（位置/朝向）和视觉子节点（旋转 180°，不随逻辑翻转）；
        /// 3. 挂载 CombatEntity、技能组件、Spine 视图、范围视图、血条；
        /// 4. 注册到网格与列表，选中新干员，订阅死亡清理；
        /// 5. 触发格子上的敌人被该干员阻挡；
        /// 6. 扣除费用。
        /// 部署失败返回 false，不产生任何副作用。
        /// </summary>
        public bool TryDeploy(OperatorConfig operatorConfig, Vector2Int cell, GridFacing facing)
        {
            if (!CanDeployAt(operatorConfig, cell))
            {
                return false;
            }
            // 取预制体（可能为空，用兜底立方体代替）
            GameObject prefab = _unitCatalog != null
                ? _unitCatalog.GetOperator(operatorConfig.operatorId)
                : null;
            float surfaceHeight = _grid.Map.GetSurfaceHeight(cell);

            // 逻辑根节点负责格子/范围，视觉子节点单独旋转 180°，避免攻击范围跟着图片翻转。
            GameObject go = new GameObject("Ally_" + operatorConfig.operatorId);
            go.transform.position = _grid.Map.CellToWorld(cell) + Vector3.up * surfaceHeight;
            go.transform.rotation = Quaternion.Euler(0f, GetFacingYaw(facing), 0f);
            // 创建视觉子节点
            GameObject visual;
            if (prefab != null)
            {
                visual = Instantiate(prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                // 无预制体时创建兜底立方体，使用干员调试色，并移除碰撞体（部署判定不走物理）
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = Vector3.up * 0.4f;
                visual.transform.localScale = Vector3.one * 0.8f;
                Renderer renderer = visual.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateMaterial(operatorConfig.debugColor);

                Collider collider = visual.GetComponent<Collider>();
                if (collider != null)
                {
                    BattleObjectUtility.Destroy(collider);
                }
            }
            // 挂载并初始化战斗实体
            CombatEntity entity = go.AddComponent<CombatEntity>();
            entity.Initialize(World, Faction.Ally, operatorConfig, cell, facing);
            // 技能组件
            SkillComponent skillComponent = go.AddComponent<SkillComponent>();
            skillComponent.Initialize(entity, operatorConfig.skills);
            entity.AttachSkills(skillComponent);
            // 若预制体带 Spine 视图，绑定实体（含未激活的子对象）
            SpineUnitView spineView = visual.GetComponentInChildren<SpineUnitView>(true);
            if (spineView != null)
            {
                spineView.Bind(entity);
            }
            // 攻击范围可视化（以逻辑范围为准）
            OperatorRangeView rangeView = go.AddComponent<OperatorRangeView>();
            rangeView.Initialize(operatorConfig.rangeCells, -surfaceHeight, 1f);
            UnitStatusBarFactory.Attach(go.transform, entity);// 血条
            // 注册到网格与列表，通知 UI，并选中新干员
            _grid.RegisterAlly(cell, entity);
            _allies.Add(entity);
            AlliesChanged?.Invoke();
            SelectAlly(entity);
            // 订阅死亡事件：清理网格/列表/选中/血条/对象
            entity.Health.Died += () =>
            {
                _grid.RemoveAlly(cell);
                _allies.Remove(entity);
                AlliesChanged?.Invoke();
                if (SelectedAlly == entity)
                {
                    SelectedAlly = null;
                }

                UnitStatusBarFactory.Release(go.transform);
                BattleObjectUtility.Destroy(go);
            };
            // 让目标格上的敌人被这个干员阻挡（缓冲复用，避免每次 new List）
            _grid.GetEnemiesAt(cell, _enemyBuffer);
            for (int i = 0; i < _enemyBuffer.Count; i++)
            {
                EnemyUnit enemy = _enemyBuffer[i].GetComponent<EnemyUnit>();
                enemy?.TryBlockBy(entity);
            }
            // 扣除费用并通知 UI
            _cost -= operatorConfig.deployCost;
            CostChanged?.Invoke();
            return true;
        }
        /// <summary>
        /// 设置当前选中的干员；空引用或已死亡视为取消选中。
        /// 选中未变化时不触发事件。
        /// </summary>
        public void SelectAlly(CombatEntity ally)
        {
            CombatEntity next = ally != null && ally.IsAlive ? ally : null;
            if (ReferenceEquals(SelectedAlly, next))
            {
                return;
            }

            SelectedAlly = next;
            SelectionChanged?.Invoke(next);
        }
        /// <summary>
        /// 尝试撤退（手动移除）指定干员：
        /// 先释放阻挡，再清理网格/列表/选中/血条/对象，返回是否成功。
        /// </summary>
        public bool TryRetreat(CombatEntity ally)
        {
            if (ally == null || !_allies.Contains(ally))
            {
                return false;
            }

            Vector2Int cell = ally.GridPosition;

            // 先释放阻挡并触发事件，敌人可以立刻清空目标；然后再销毁干员。
            ally.ReleaseAllBlocks();
            _grid.RemoveAlly(cell);
            _allies.Remove(ally);
            AlliesChanged?.Invoke();

            if (SelectedAlly == ally)
            {
                SelectedAlly = null;
            }

            UnitStatusBarFactory.Release(ally.transform);
            BattleObjectUtility.Destroy(ally.gameObject);
            return true;
        }
        #endregion

        #region 调试
        /// <summary>调试/演示用：立即判定胜利，之后走正常的结算与掉落流程。</summary>
        public void ForceVictory()
        {
            if (State != BattleState.Running)
            {
                return;
            }

            State = BattleState.Victory;
            Debug.Log("[Battle] 强制胜利（调试工具）");
        }
        #endregion

        #region 道具
        /// <summary>查询道具剩余次数；未知道具返回 0。</summary>
        public int GetItemCharges(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.id))
            {
                return 0;
            }

            return _itemCharges.TryGetValue(item.id, out int charges) ? charges : 0;
        }

        /// <summary>是否还能使用该道具（战斗中且剩余次数大于 0）。</summary>
        public bool CanUseItem(ItemDefinition item)
        {
            return State == BattleState.Running && GetItemCharges(item) > 0;
        }

        /// <summary>在空闲格使用道具：派发效果，成功后扣一次并通知 UI。</summary>
        public bool TryUseItem(ItemDefinition item, Vector2Int cell)
        {
            if (!CanUseItem(item) || _grid == null || !_grid.IsItemPlaceable(cell))
            {
                return false;
            }

            var context = new ItemEffectContext(this, item, cell);
            if (!ItemEffectRegistry.TryExecute(context))
            {
                return false;
            }

            _itemCharges[item.id] = GetItemCharges(item) - 1;
            ItemsChanged?.Invoke();
            return true;
        }
        #endregion

        #region 战斗主循环
        /// <summary>
        /// 战斗主循环（由 CombatWorld.Ticked 驱动）：
        /// 1. 恢复费用；
        /// 2. 推进波次（按节奏生成敌人）；
        /// 3. 逐个 Tick 存活敌人；
        /// 4. 判定失败（生命归零）和胜利（波次全部生成且无存活敌人）。
        /// </summary>
        private void OnTick(int tick)
        {
            if (State != BattleState.Running)
            {
                return;
            }

            RegenerateCost();

            _waveManager.Tick(tick);
            // 倒序遍历，允许 Tick 中移除敌人
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                EnemyUnit enemy = _enemies[i];
                if (enemy != null)
                {
                    enemy.Tick();
                }
            }
            // 失败判定
            if (_life <= 0)
            {
                State = BattleState.Defeat;
                return;
            }
            // 胜利判定：所有波次已生成，且场上无存活敌人
            if (_waveManager.AllWavesSpawned && _enemies.Count == 0)
            {
                State = BattleState.Victory;
            }
        }
        /// <summary>
        /// 按设置恢复费用：使用浮点累积器，累计满 1 才增加费用，并限制最大费用。
        /// 费用无变化时不触发事件。
        /// </summary>
        private void RegenerateCost()
        {
            if (_settings == null || _settings.costRegenPerSecond <= 0f)
            {
                return;
            }

            _costAccumulator += _settings.costRegenPerSecond * CombatConstants.TickSeconds;
            int gained = Mathf.FloorToInt(_costAccumulator);
            if (gained <= 0)
            {
                return;
            }

            _costAccumulator -= gained;
            int nextCost = Mathf.Min(_settings.maxCost, _cost + gained);
            if (nextCost == _cost)
            {
                return;
            }

            _cost = nextCost;
            CostChanged?.Invoke();
        }
        #endregion

        #region 内部辅助
        /// <summary>
        /// 内部回收：把敌人从网格和敌人列表移除，按需释放/销毁对象。
        /// 调用方负责先更新已解决计数和 UI。
        /// </summary>
        private void RemoveEnemy(EnemyUnit enemy, bool destroy)
        {
            if (enemy == null)
            {
                return;
            }

            if (enemy.Entity != null)
            {
                _grid.RemoveEnemy(enemy.Entity);
            }

            _enemies.Remove(enemy);
            EnemiesChanged?.Invoke();

            if (destroy)
            {
                if (_enemyFactory != null)
                {
                    // 敌人根节点（含血条）整条回收，下一波直接复用。
                    _enemyFactory.Release(enemy);
                }
                else
                {
                    UnitStatusBarFactory.Release(enemy.transform);
                    BattleObjectUtility.Destroy(enemy.gameObject);
                }
            }
        }
        /// <summary>
        /// 创建运行时兜底材质：按 Standard → URP/Lit → Sprites/Default 顺序查找 shader，
        /// 覆盖内置管线、URP 和 2D 场景。材质登记到 _runtimeAssets，OnDestroy 时统一销毁。
        /// </summary>
        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader);
            material.color = color;
            _runtimeAssets.Add(material);
            return material;
        }
        /// <summary>
        /// 把网格朝向转换为 Y 轴旋转角：
        /// Up=270, Left=180, Down=90, Right=0（默认）。
        /// 默认 0 对应"右"，是 2D 素材的常见默认朝向。
        /// </summary>
        private static float GetFacingYaw(GridFacing facing)
        {
            switch (facing)
            {
                case GridFacing.Up:
                    return 270f;
                case GridFacing.Left:
                    return 180f;
                case GridFacing.Down:
                    return 90f;
                default:
                    return 0f;
            }
        }
        #endregion

        #region Unity 生命周期
        /// <summary>
        /// 销毁时：反订阅 Tick、释放敌人工厂、销毁运行时材质，避免泄漏。
        /// </summary>
        private void OnDestroy()
        {
            if (World != null)
            {
                World.Ticked -= OnTick;
            }

            _enemyFactory?.Dispose();

            for (int i = 0; i < _runtimeAssets.Count; i++)
            {
                if (_runtimeAssets[i] != null)
                {
                    BattleObjectUtility.Destroy(_runtimeAssets[i]);
                }
            }

            _runtimeAssets.Clear();
        }
        #endregion
    }
}
