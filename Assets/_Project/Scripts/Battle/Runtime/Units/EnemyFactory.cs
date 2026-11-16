/****************************************************
    文件：EnemyFactory.cs
    作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
    功能：按敌人配置创建敌人实体；每场战斗一个对象池，按敌人 ID 复用整条敌人根节点。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 敌人工厂：负责敌人的创建、配置构建与对象池回收。
    ///
    /// 生命周期：
    /// - 构造时只登记敌人定义（EnemyDefinition），不创建任何对象；
    /// - Prewarm 按当前关卡波次实际出现的敌人预热池；
    /// - Create 优先从池中取，取不到再新建"空壳"（CreateShell）；
    /// - Release 死亡/漏怪后回收整条敌人根节点，超过上限直接销毁；
    /// - Dispose 战斗结束时销毁池中所有对象与运行时创建的 ScriptableObject。
    ///
    /// 池的粒度是"整条敌人根节点"（含视觉、血条、组件），
    /// 这样复用时不产生 GameObject 重建成本，只需重置状态。
    /// </summary>
    public sealed class EnemyFactory
    {
        #region 常量
        // 每种敌人最多缓存 30 个，避免极端配置把内存撑爆。
        private const int MaxPoolPerEnemy = 30;
        #endregion

        #region 字段
        // 外部依赖：战斗总控（敌人初始化时需要）、网格（站位/路径）、预制体目录
        private readonly BattleController _controller;
        private readonly BattleGrid _grid;
        private readonly UnitPrefabCatalog _catalog;
        // 敌人定义表：enemyId → EnemyDefinition（构造时从 EnemyLibrary 登记）
        private readonly Dictionary<int, EnemyDefinition> _definitions =
            new Dictionary<int, EnemyDefinition>();
        // 敌人运行时配置缓存：enemyId → EnemyConfig（ScriptableObject，首次创建后复用）
        private readonly Dictionary<int, EnemyConfig> _configs =
            new Dictionary<int, EnemyConfig>();
        // 对象池：enemyId → 空闲 EnemyUnit 栈
        private readonly Dictionary<int, Stack<EnemyUnit>> _pools =
            new Dictionary<int, Stack<EnemyUnit>>();
        // 运行时动态创建的 Unity 对象（EnemyConfig、AttackData、兜底材质），
        // Dispose 时统一销毁，避免泄漏
        private readonly List<Object> _runtimeAssets = new List<Object>();
        #endregion

        #region 构造

        /// <summary>
        /// 构造工厂：登记所有合法的敌人定义（id > 0）。
        /// 不创建任何运行时对象，真正的创建发生在 Prewarm / Create。
        /// </summary>
        public EnemyFactory(BattleController controller, BattleGrid grid, EnemyLibrary library)
        {
            _controller = controller;
            _grid = grid;
            _catalog = AssetService.Load<UnitPrefabCatalog>("UnitPrefabCatalog");

            if (library != null && library.enemies != null)
            {
                for (int i = 0; i < library.enemies.Count; i++)
                {
                    EnemyDefinition definition = library.enemies[i];
                    // 只登记 id 有效的定义；id <= 0 视为无效配置
                    if (definition != null && definition.id > 0)
                    {
                        _definitions[definition.id] = definition;
                    }
                }
            }
        }
        #endregion

        #region 预热
        /// <summary>
        /// 按当前关卡波次里实际出现的敌人预热。只预热 prewarmCount > 1 的敌人，
        /// 数量上限 30；Boss/稀有怪把 prewarmCount 填 1 就不会预热。
        ///
        /// 流程：
        /// 1. 遍历所有波次，收集实际会出现的 enemyId（HashSet 去重）；
        /// 2. 对每个 id，按定义的 prewarmCount 决定预热数量（未配置时默认 10）；
        /// 3. prewarmCount <= 1 视为"不预热"（Boss/稀有怪）；
        /// 4. 创建空壳、设为非激活、压入对应池，直到达到目标数量。
        /// </summary>
        public void Prewarm(WaveLibrary waves)
        {
            if (waves == null || waves.waves == null)
            {
                return;
            }
            // 1. 收集本局实际会出现的敌人 ID（去重）
            var usedIds = new HashSet<int>();
            for (int i = 0; i < waves.waves.Count; i++)
            {
                WaveDefinition wave = waves.waves[i];
                if (wave == null || wave.spawns == null)
                {
                    continue;
                }

                for (int s = 0; s < wave.spawns.Count; s++)
                {
                    WaveSpawnDefinition spawn = wave.spawns[s];
                    if (spawn != null && spawn.count > 0)
                    {
                        usedIds.Add(spawn.enemyId);
                    }
                }
            }
            // 2. 按每个敌人的 prewarmCount 预热
            foreach (int id in usedIds)
            {
                if (!_definitions.TryGetValue(id, out EnemyDefinition definition))
                {
                    continue;// 波次里引用了未定义的敌人，跳过（Create 时会报错）
                }
                // prewarmCount 未配置时默认 10；<= 1 视为不预热
                int wanted = definition.prewarmCount <= 0 ? 10 : definition.prewarmCount;
                if (wanted <= 1)
                {
                    continue;
                }
                // 上限保护，避免配置写太大把内存撑爆
                int target = Mathf.Min(wanted, MaxPoolPerEnemy);
                Stack<EnemyUnit> pool = GetPool(id);
                while (pool.Count < target)
                {
                    EnemyUnit shell = CreateShell(definition);
                    shell.gameObject.SetActive(false);// 预热对象先隐藏
                    pool.Push(shell);
                }
            }
        }
        #endregion

        #region 创建

        /// <summary>
        /// 按 enemyId 创建敌人并沿 route 移动：
        /// 1. 查定义与路线合法性（路线至少 2 个点）；
        /// 2. 取/建运行时 EnemyConfig；
        /// 3. 从池中取，取不到则新建空壳；
        /// 4. 激活、初始化 CombatEntity、绑定 Spine 视图、挂血条。
        /// 失败（定义缺失/路线为空）时返回 null，不抛异常。
        /// </summary>
        public EnemyUnit Create(int enemyId, List<Vector2Int> route)
        {
            if (!_definitions.TryGetValue(enemyId, out EnemyDefinition definition))
            {
                Debug.LogError("找不到敌人配置：" + enemyId);
                return null;
            }

            if (route == null || route.Count < 2)
            {
                Debug.LogError("敌人路线为空，取消生成：" + enemyId);
                return null;
            }
            // 运行时配置按 enemyId 缓存，多只同类型敌人共享同一份 EnemyConfig
            EnemyConfig config = GetOrCreateConfig(definition);
            // 优先从池中取；池空则新建
            EnemyUnit enemy = Rent(enemyId);
            if (enemy == null)
            {
                enemy = CreateShell(definition);
            }

            enemy.gameObject.SetActive(true);
            CombatEntity entity = enemy.GetComponent<CombatEntity>();
            // 初始化战斗实体：注入控制器、网格、实体引用、配置与路线
            enemy.Initialize(_controller, _grid, entity, config, route);
            // 若预制体带 Spine 视图，绑定实体（含未激活的子对象）
            SpineUnitView spineView = enemy.GetComponentInChildren<SpineUnitView>(true);
            if (spineView != null)
            {
                spineView.Bind(entity);
            }
            // 血条跟随敌人根节点
            UnitStatusBarFactory.Attach(enemy.transform, entity);
            return enemy;
        }

        #endregion

        #region 回收

        /// <summary>
        /// 死亡/漏怪后回收整条敌人根节点：
        /// 1. 调用 ReleaseForPool 重置敌人自身状态；
        /// 2. 设为非激活；
        /// 3. 池未满则入池复用，池满则直接销毁。
        /// </summary>
        public void Release(EnemyUnit enemy)
        {
            if (enemy == null)
            {
                return;
            }
            // 先重置敌人内部状态（清空目标、路径索引、血条等），再入池
            enemy.ReleaseForPool();
            enemy.gameObject.SetActive(false);

            Stack<EnemyUnit> pool = GetPool(enemy.EnemyId);
            if (pool.Count >= MaxPoolPerEnemy)
            {
                // 池已满，直接销毁，避免无限增长
                BattleObjectUtility.Destroy(enemy.gameObject);
                return;
            }

            pool.Push(enemy);
        }
        #endregion

        #region 空壳创建

        /// <summary>
        /// 创建一个"空壳"敌人：根节点 + 视觉子节点 + CombatEntity + EnemyUnit 组件。
        /// 不初始化配置与路线，真正的初始化在 Create 里做。
        /// 视觉优先用预制体，缺失时用兜底立方体 + 调试色材质。
        /// </summary>
        private EnemyUnit CreateShell(EnemyDefinition definition)
        {
            GameObject prefab = _catalog != null ? _catalog.GetEnemy(definition.id) : null;
            GameObject go = new GameObject("Enemy_" + definition.id);
            // 视觉子节点：预制体或兜底立方体
            GameObject visual;
            if (prefab != null)
            {
                visual = Object.Instantiate(prefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);// 2D 素材默认朝右，翻转 180° 朝左
            }
            else
            {
                visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.SetParent(go.transform, false);
                visual.transform.localPosition = Vector3.up * 0.4f;
                visual.transform.localScale = Vector3.one * 0.75f;
                Renderer renderer = visual.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateMaterial(
                    new Color(definition.colorR, definition.colorG, definition.colorB));
                // 移除碰撞体，敌人判定不走物理
                Collider collider = visual.GetComponent<Collider>();
                if (collider != null)
                {
                    BattleObjectUtility.Destroy(collider);
                }
            }
            // 挂载战斗实体与敌人单位组件
            go.AddComponent<CombatEntity>();
            return go.AddComponent<EnemyUnit>();
        }
        #endregion

        #region 对象池

        /// <summary>
        /// 获取指定敌人的池；不存在则创建空池。
        /// </summary>
        private Stack<EnemyUnit> GetPool(int enemyId)
        {
            if (!_pools.TryGetValue(enemyId, out Stack<EnemyUnit> pool))
            {
                pool = new Stack<EnemyUnit>();
                _pools[enemyId] = pool;
            }

            return pool;
        }
        /// <summary>
        /// 从池中取一个可用对象；池空或取到 null（Unity 假 null）时返回 null。
        /// 用 while 循环是因为池中可能有已被销毁的"假 null"对象。
        /// </summary>
        private EnemyUnit Rent(int enemyId)
        {
            Stack<EnemyUnit> pool = GetPool(enemyId);
            while (pool.Count > 0)
            {
                EnemyUnit pooled = pool.Pop();
                if (pooled != null)
                {
                    return pooled;
                }
            }

            return null;
        }
        #endregion

        #region 运行时配置构建

        /// <summary>
        /// 获取或创建敌人运行时配置（EnemyConfig）。
        /// 同一 enemyId 只创建一次，之后复用；配置登记到 _runtimeAssets，Dispose 时销毁。
        /// 注意：rangeCells 固定为 {zero}（只打自身格），blockCount 固定为 0（敌人不阻挡别人）。
        /// </summary>
        private EnemyConfig GetOrCreateConfig(EnemyDefinition definition)
        {
            if (_configs.TryGetValue(definition.id, out EnemyConfig config))
            {
                return config;
            }

            config = ScriptableObject.CreateInstance<EnemyConfig>();
            config.name = "Runtime Enemy " + definition.id;
            config.hideFlags = HideFlags.DontSave;
            config.enemyId = definition.id;
            config.displayName = definition.displayName;
            config.maxHealth = definition.maxHealth;
            config.attack = definition.attack;
            config.defense = definition.defense;
            config.magicResist = definition.magicResist;
            config.attackIntervalTicks = definition.attackIntervalTicks;
            config.windupTicks = definition.windupTicks;
            config.attackRange = 1f;
            config.rangeCells = new[] { Vector2Int.zero };// 敌人攻击范围固定为自身格
            config.moveSpeed = definition.moveSpeed;
            config.blockWeight = definition.blockWeight;
            config.lifeDamage = definition.lifeDamage;
            config.blockCount = 0;// 敌人不阻挡其他单位
            config.normalAttack = CreateAttack(definition);
            _configs[definition.id] = config;
            _runtimeAssets.Add(config);
            return config;
        }
        /// <summary>
        /// 为敌人创建普通攻击数据（AttackData），固定单体单目标。
        /// 登记到 _runtimeAssets 统一销毁。
        /// </summary>
        private AttackData CreateAttack(EnemyDefinition definition)
        {
            AttackData attack = ScriptableObject.CreateInstance<AttackData>();
            attack.name = "Runtime Enemy Attack " + definition.id;
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
        /// 创建兜底材质：按 Standard → URP/Lit → Sprites/Default 顺序查找 shader，
        /// 覆盖内置管线、URP 和 2D 场景。材质登记到 _runtimeAssets，Dispose 时销毁。
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
        #endregion

        #region 清理

        /// <summary>
        /// 战斗结束时清理：
        /// 1. 销毁池中所有空闲敌人对象；
        /// 2. 清空池字典；
        /// 3. 销毁运行时创建的 ScriptableObject 与材质。
        /// 注意：只处理池中的空闲对象，场上仍存活的敌人由 BattleController 负责。
        /// </summary>
        public void Dispose()
        {
            foreach (Stack<EnemyUnit> pool in _pools.Values)
            {
                while (pool.Count > 0)
                {
                    EnemyUnit pooled = pool.Pop();
                    if (pooled != null)
                    {
                        BattleObjectUtility.Destroy(pooled.gameObject);
                    }
                }
            }

            _pools.Clear();

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
