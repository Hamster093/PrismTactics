/****************************************************
    文件：M0BattleBootstrap.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:25:21
	功能：M0 演示启动器：创建色块战场、运行时配置与战斗 HUD。
*****************************************************/

using System.Collections.Generic;
using System.Text;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Game
{
    /// <summary>
    /// M0 纵切片：四个色块单位、固定 30Hz 战斗 tick、索敌、
    /// 前摇/后摇和数据驱动的伤害管线。
    /// 暂不包含寻路、阻挡和波次，这些属于 M1/M3。
    /// </summary>
    public sealed class M0BattleBootstrap : MonoBehaviour
    {
        [SerializeField] private OperatorConfig _allyConfig;
        [SerializeField] private OperatorConfig _enemyConfig;
        [SerializeField] private bool _buildOnStart = true;
        [SerializeField] private bool _configureCamera = true;

        private readonly List<CombatEntity> _spawned = new List<CombatEntity>();
        private readonly List<Object> _runtimeAssets = new List<Object>();
        private readonly StringBuilder _hudText = new StringBuilder(512);

        private CombatWorld _world;
        private GUIStyle _labelStyle;

        private void Start()
        {
            if (_buildOnStart)
            {
                Build();
            }
        }

        [ContextMenu("生成 M0 演示")]
        public void Build()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("M0BattleBootstrap.Build 只能在 Play Mode 下运行。");
                return;
            }

            if (_world != null)
            {
                return;
            }

            _world = FindObjectOfType<CombatWorld>();
            if (_world == null)
            {
                _world = new GameObject("Combat World").AddComponent<CombatWorld>();
            }

            EnsureCameraAndLight();
            BuildArena();

            OperatorConfig allyConfig = _allyConfig != null ? _allyConfig : CreateAllyConfig();
            OperatorConfig enemyConfig = _enemyConfig != null ? _enemyConfig : CreateEnemyConfig();

            Spawn("Ally_A", Faction.Ally, allyConfig, new Vector3(-1.8f, 0.5f, 0f), new Color(0.18f, 0.55f, 1f));
            Spawn("Ally_B", Faction.Ally, allyConfig, new Vector3(-1.8f, 0.5f, 2.2f), new Color(0.2f, 0.8f, 1f));
            Spawn("Enemy_A", Faction.Enemy, enemyConfig, new Vector3(1.8f, 0.5f, 0f), new Color(1f, 0.25f, 0.2f));
            Spawn("Enemy_B", Faction.Enemy, enemyConfig, new Vector3(1.8f, 0.5f, 2.2f), new Color(1f, 0.55f, 0.15f));

            Debug.Log("[M0] 战斗开始：2 个友方 vs 2 个敌方，固定 30Hz tick。");
        }

        private void BuildArena()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Arena Ground";
            ground.transform.position = new Vector3(0f, -0.1f, 1.1f);
            ground.transform.localScale = new Vector3(12f, 0.2f, 8f);
            ground.GetComponent<Renderer>().sharedMaterial = CreateMaterial(new Color(0.12f, 0.14f, 0.17f));
            RemoveCollider(ground);

            CreateLaneMarker("Ally Lane", new Vector3(-1.8f, 0.005f, 1.1f), new Color(0.15f, 0.35f, 0.65f));
            CreateLaneMarker("Enemy Lane", new Vector3(1.8f, 0.005f, 1.1f), new Color(0.6f, 0.16f, 0.14f));
        }

        private void CreateLaneMarker(string name, Vector3 position, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = name;
            marker.transform.position = position;
            marker.transform.localScale = new Vector3(1.5f, 0.01f, 5.5f);
            marker.GetComponent<Renderer>().sharedMaterial = CreateMaterial(color);
            RemoveCollider(marker);
        }

        private void Spawn(string unitName, Faction faction, OperatorConfig config, Vector3 position, Color color)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = unitName;
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 0.9f;
            go.GetComponent<Renderer>().sharedMaterial = CreateMaterial(color);
            RemoveCollider(go);

            CombatEntity entity = go.AddComponent<CombatEntity>();
            entity.Initialize(_world, faction, config);
            _spawned.Add(entity);
        }

        private void EnsureCameraAndLight()
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
                camera.transform.position = new Vector3(0f, 10f, -12f);
                camera.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            }

            if (FindObjectOfType<Light>() == null)
            {
                GameObject lightGo = new GameObject("Directional Light");
                Light directional = lightGo.AddComponent<Light>();
                directional.type = LightType.Directional;
                directional.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private OperatorConfig CreateAllyConfig()
        {
            OperatorConfig config = CreateInstance<OperatorConfig>();
            config.name = "Runtime Ally Config";
            config.operatorId = "ally_demo";
            config.displayName = "Ally";
            config.maxHealth = 140f;
            config.attack = 18f;
            config.defense = 6f;
            config.magicResist = 0f;
            config.attackIntervalTicks = 30;
            config.windupTicks = 9;
            config.attackRange = 3.8f;
            config.targetSelector = TargetSelectorType.Nearest;
            config.normalAttack = CreateAttack("Runtime Ally Attack", DamageType.Physical, 0f, 1f, DamageShape.Single, 0f, 1);
            return config;
        }

        private OperatorConfig CreateEnemyConfig()
        {
            OperatorConfig config = CreateInstance<OperatorConfig>();
            config.name = "Runtime Enemy Config";
            config.operatorId = "enemy_demo";
            config.displayName = "Enemy";
            config.maxHealth = 110f;
            config.attack = 12f;
            config.defense = 2f;
            config.magicResist = 0f;
            config.attackIntervalTicks = 40;
            config.windupTicks = 12;
            config.attackRange = 3.8f;
            config.targetSelector = TargetSelectorType.Nearest;
            config.normalAttack = CreateAttack("Runtime Enemy Attack", DamageType.Physical, 0f, 1f, DamageShape.Splash, 1.6f, 2);
            return config;
        }

        private AttackData CreateAttack(
            string assetName,
            DamageType damageType,
            float baseDamage,
            float attackScale,
            DamageShape shape,
            float splashRadius,
            int maxTargets)
        {
            AttackData attack = CreateInstance<AttackData>();
            attack.name = assetName;
            attack.damageType = damageType;
            attack.baseDamage = baseDamage;
            attack.attackScale = attackScale;
            attack.damageShape = shape;
            attack.splashRadius = splashRadius;
            attack.maxTargets = maxTargets;
            return attack;
        }

        private T CreateInstance<T>() where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.hideFlags = HideFlags.DontSave;
            _runtimeAssets.Add(asset);
            return asset;
        }

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

        private static void RemoveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void OnGUI()
        {
            if (_world == null)
            {
                return;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = 13;
                _labelStyle.normal.textColor = Color.white;
            }

            _hudText.Clear();
            _hudText.AppendLine("M0 战斗 | 固定 30Hz tick");
            _hudText.Append("tick=").Append(_world.Clock.TickCount)
                .Append("  实体=").Append(_world.Registry.Count).AppendLine();

            IReadOnlyList<CombatEntity> entities = _world.Registry.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                CombatEntity entity = entities[i];
                if (entity == null)
                {
                    continue;
                }

                _hudText.Append(entity.name).Append("  ")
                    .Append(GetFactionLabel(entity.Faction)).Append("  生命 ")
                    .Append(entity.CurrentHealth.ToString("0")).Append("/")
                    .Append(entity.MaxHealth.ToString("0"));

                if (entity.AttackCycle != null)
                {
                    _hudText.Append("  ").Append(GetStateLabel(entity.AttackCycle.State));
                }

                if (entity.CurrentTarget != null)
                {
                    _hudText.Append(" -> ").Append(entity.CurrentTarget.name);
                }

                _hudText.AppendLine();
            }

            float height = 34f + entities.Count * 20f;
            GUI.Box(new Rect(10f, 10f, 470f, height), GUIContent.none);
            GUI.Label(new Rect(22f, 18f, 450f, height), _hudText.ToString(), _labelStyle);
        }

        private static string GetFactionLabel(Faction faction)
        {
            switch (faction)
            {
                case Faction.Ally:
                    return "友方";
                case Faction.Enemy:
                    return "敌方";
                default:
                    return "中立";
            }
        }

        private static string GetStateLabel(AttackCycleState state)
        {
            switch (state)
            {
                case AttackCycleState.Ready:
                    return "待机";
                case AttackCycleState.Windup:
                    return "前摇";
                case AttackCycleState.Recovery:
                    return "后摇";
                default:
                    return state.ToString();
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimeAssets.Count; i++)
            {
                if (_runtimeAssets[i] != null)
                {
                    Destroy(_runtimeAssets[i]);
                }
            }

            _runtimeAssets.Clear();
        }
    }
}
