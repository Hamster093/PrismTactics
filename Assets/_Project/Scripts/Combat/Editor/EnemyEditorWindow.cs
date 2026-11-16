/****************************************************
    文件：EnemyEditorWindow.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 15:10:00
	功能：怪物编辑器：用数字编号配置敌人属性、攻击、移动与漏怪参数。
*****************************************************/

using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public sealed class EnemyEditorWindow : EditorWindow
    {
        private const string EnemyPath = "Assets/GameContent/Configs/Units/enemies.json";

        private EnemyLibrary _library;
        private int _selectedIndex = -1;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _status = string.Empty;

        [MenuItem("Tools/PrismTactics/1 内容编辑/1.2 单位与地图/1.2.1 怪物编辑器")]
        public static void Open()
        {
            var window = GetWindow<EnemyEditorWindow>("PrismTactics 怪物编辑器");
            window.minSize = new Vector2(760f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            Load();
        }

        private void OnGUI()
        {
            if (_library == null)
            {
                Load();
            }

            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawList();
            DrawDetail();
            EditorGUILayout.EndHorizontal();
            DrawStatus();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("读取 JSON", EditorStyles.toolbarButton))
            {
                Load();
            }

            if (GUILayout.Button("保存 JSON", EditorStyles.toolbarButton))
            {
                Save();
            }

            if (GUILayout.Button("新增怪物", EditorStyles.toolbarButton))
            {
                AddEnemy();
            }

            if (GUILayout.Button("复制怪物", EditorStyles.toolbarButton))
            {
                DuplicateEnemy();
            }

            if (GUILayout.Button("删除怪物", EditorStyles.toolbarButton))
            {
                DeleteEnemy();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(EnemyPath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220f));
            EditorGUILayout.LabelField("怪物列表", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

            if (_library.enemies != null)
            {
                for (int i = 0; i < _library.enemies.Count; i++)
                {
                    EnemyDefinition definition = _library.enemies[i];
                    string label = definition != null
                        ? $"#{definition.id}  {definition.displayName}"
                        : "(空)";

                    if (GUILayout.Toggle(_selectedIndex == i, label, "Button"))
                    {
                        _selectedIndex = i;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDetail()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EnemyDefinition definition = GetSelected();
            if (definition == null)
            {
                EditorGUILayout.HelpBox("左侧选择一个怪物，或点击“新增怪物”。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
            definition.id = EditorGUILayout.IntField("数字编号 ID", definition.id);
            definition.displayName = EditorGUILayout.TextField("显示名称", definition.displayName);
            Color color = EditorGUILayout.ColorField(
                "色块颜色",
                new Color(definition.colorR, definition.colorG, definition.colorB));
            definition.colorR = color.r;
            definition.colorG = color.g;
            definition.colorB = color.b;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("属性", EditorStyles.boldLabel);
            definition.maxHealth = EditorGUILayout.FloatField("生命", definition.maxHealth);
            definition.attack = EditorGUILayout.FloatField("攻击", definition.attack);
            definition.defense = EditorGUILayout.FloatField("防御", definition.defense);
            definition.magicResist = EditorGUILayout.FloatField("法抗", definition.magicResist);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("攻击与移动", EditorStyles.boldLabel);
            definition.attackIntervalTicks = EditorGUILayout.IntField(
                "攻击间隔 tick",
                definition.attackIntervalTicks);
            definition.windupTicks = EditorGUILayout.IntField("前摇 tick", definition.windupTicks);
            definition.damageType = (int)(DamageType)EditorGUILayout.EnumPopup(
                "伤害类型",
                (DamageType)definition.damageType);
            definition.baseDamage = EditorGUILayout.FloatField("固定伤害", definition.baseDamage);
            definition.attackScale = EditorGUILayout.FloatField("攻击倍率", definition.attackScale);
            definition.moveSpeed = EditorGUILayout.FloatField("移动速度（格/秒）", definition.moveSpeed);
            definition.blockWeight = EditorGUILayout.IntField("阻挡权重", definition.blockWeight);
            definition.lifeDamage = EditorGUILayout.IntField("漏怪扣生命", definition.lifeDamage);
            definition.prewarmCount = Mathf.Clamp(
                EditorGUILayout.IntField("预热数量（1=不预热，上限30）", definition.prewarmCount),
                1,
                30);

            EditorGUILayout.Space(8f);
            float seconds = definition.attackIntervalTicks / (float)CombatConstants.TickRate;
            EditorGUILayout.HelpBox(
                $"编号 #{definition.id}\n生命 {definition.maxHealth:0}  攻击 {definition.attack:0}  " +
                $"防御 {definition.defense:0}  法抗 {definition.magicResist:0}\n" +
                $"攻击间隔 {seconds:0.00} 秒，移动 {definition.moveSpeed:0.00} 格/秒",
                MessageType.None);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private EnemyDefinition GetSelected()
        {
            if (_library == null || _library.enemies == null
                || _selectedIndex < 0 || _selectedIndex >= _library.enemies.Count)
            {
                return null;
            }

            return _library.enemies[_selectedIndex];
        }

        private void Load()
        {
            _library = LoadLibrary<EnemyLibrary>(EnemyPath);
            if (_library == null)
            {
                _library = new EnemyLibrary();
            }

            if (_library.enemies == null)
            {
                _library.enemies = new List<EnemyDefinition>();
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.enemies.Count - 1);
            _status = "已读取 " + EnemyPath;
        }

        private static T LoadLibrary<T>(string assetPath) where T : class
        {
            string absolutePath = Path.Combine(
                Application.dataPath,
                assetPath.Substring("Assets/".Length));

            if (!File.Exists(absolutePath))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<T>(File.ReadAllText(absolutePath));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("读取怪物配置失败：" + assetPath + "\n" + exception.Message);
                return null;
            }
        }

        private void Save()
        {
            if (!Validate(out string error))
            {
                _status = "无法保存：" + error;
                return;
            }

            string absolutePath = Path.Combine(
                Application.dataPath,
                EnemyPath.Substring("Assets/".Length));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(_library, true));
            AssetDatabase.Refresh();
            _status = "已保存 " + EnemyPath + "，重新 Play 后生效。";
        }

        private bool Validate(out string error)
        {
            var ids = new HashSet<int>();
            for (int i = 0; i < _library.enemies.Count; i++)
            {
                EnemyDefinition definition = _library.enemies[i];
                if (definition == null)
                {
                    continue;
                }

                if (definition.id <= 0)
                {
                    error = $"第 {i + 1} 个怪物的 ID 必须是正整数。";
                    return false;
                }

                if (!ids.Add(definition.id))
                {
                    error = "怪物数字 ID 重复：" + definition.id;
                    return false;
                }

                if (definition.maxHealth <= 0f)
                {
                    error = "怪物 #" + definition.id + " 的生命必须大于 0。";
                    return false;
                }

                if (definition.attackIntervalTicks <= 0)
                {
                    error = "怪物 #" + definition.id + " 的攻击间隔必须大于 0。";
                    return false;
                }

                if (definition.windupTicks > definition.attackIntervalTicks)
                {
                    error = "怪物 #" + definition.id + " 的前摇不能大于攻击间隔。";
                    return false;
                }

                if (definition.moveSpeed <= 0f)
                {
                    error = "怪物 #" + definition.id + " 的移动速度必须大于 0。";
                    return false;
                }

                if (definition.prewarmCount < 1 || definition.prewarmCount > 30)
                {
                    error = "怪物 #" + definition.id + " 的预热数量必须在 1~30 之间。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void AddEnemy()
        {
            int id = 1;
            while (FindEnemy(id) != null)
            {
                id++;
            }

            _library.enemies.Add(new EnemyDefinition
            {
                id = id,
                displayName = "新怪物",
                maxHealth = 60f,
                attack = 8f,
                attackIntervalTicks = 40,
                windupTicks = 12,
                moveSpeed = 1.5f,
                blockWeight = 1,
                lifeDamage = 1,
                prewarmCount = 10,
                colorR = 1f,
                colorG = 0.3f,
                colorB = 0.2f
            });
            _selectedIndex = _library.enemies.Count - 1;
        }

        private void DuplicateEnemy()
        {
            EnemyDefinition source = GetSelected();
            if (source == null)
            {
                return;
            }

            EnemyDefinition copy = JsonUtility.FromJson<EnemyDefinition>(
                JsonUtility.ToJson(source));
            copy.id = NextFreeId();
            copy.displayName = source.displayName + " 副本";
            _library.enemies.Add(copy);
            _selectedIndex = _library.enemies.Count - 1;
        }

        private void DeleteEnemy()
        {
            if (GetSelected() == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("删除怪物", "确定删除这个怪物配置吗？", "删除", "取消"))
            {
                return;
            }

            _library.enemies.RemoveAt(_selectedIndex);
            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.enemies.Count - 1);
        }

        private EnemyDefinition FindEnemy(int id)
        {
            for (int i = 0; i < _library.enemies.Count; i++)
            {
                if (_library.enemies[i] != null && _library.enemies[i].id == id)
                {
                    return _library.enemies[i];
                }
            }

            return null;
        }

        private int NextFreeId()
        {
            int id = 1;
            while (FindEnemy(id) != null)
            {
                id++;
            }

            return id;
        }
    }
}
