/****************************************************
    文件：OperatorEditorWindow.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 13:40:00
	功能：角色编辑器：配置干员基础属性、攻击范围格子、部署规则与技能绑定。
*****************************************************/

using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public sealed class OperatorEditorWindow : EditorWindow
    {
        private const string OperatorPath = "Assets/GameContent/Configs/Units/operators.json";
        private const string SkillPath = "Assets/GameContent/Configs/Skills/skills.json";
        private const int RangeRadiusX = 4;
        private const int RangeRadiusY = 3;

        private OperatorLibrary _library;
        private SkillLibrary _skillLibrary;
        private int _selectedIndex = -1;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _status = string.Empty;

        [MenuItem("Tools/PrismTactics/1 内容编辑/1.1 角色与技能/1.1.1 角色编辑器")]
        public static void Open()
        {
            var window = GetWindow<OperatorEditorWindow>("PrismTactics 角色编辑器");
            window.minSize = new Vector2(900f, 680f);
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

            if (GUILayout.Button("新增角色", EditorStyles.toolbarButton))
            {
                AddOperator();
            }

            if (GUILayout.Button("复制角色", EditorStyles.toolbarButton))
            {
                DuplicateOperator();
            }

            if (GUILayout.Button("删除角色", EditorStyles.toolbarButton))
            {
                DeleteOperator();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(OperatorPath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(220f));
            EditorGUILayout.LabelField("角色列表", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

            if (_library.operators != null)
            {
                for (int i = 0; i < _library.operators.Count; i++)
                {
                    OperatorDefinition definition = _library.operators[i];
                    string label = definition != null && !string.IsNullOrEmpty(definition.displayName)
                        ? definition.displayName
                        : "(未命名)";

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
            OperatorDefinition definition = GetSelected();
            if (definition == null)
            {
                EditorGUILayout.HelpBox("左侧选择一个角色，或点击“新增角色”。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
            definition.id = EditorGUILayout.TextField("角色 ID", definition.id);
            definition.displayName = EditorGUILayout.TextField("显示名称", definition.displayName);
            Color color = EditorGUILayout.ColorField(
                "色块颜色",
                new Color(definition.colorR, definition.colorG, definition.colorB));
            definition.colorR = color.r;
            definition.colorG = color.g;
            definition.colorB = color.b;

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("基础属性", EditorStyles.boldLabel);
            definition.maxHealth = EditorGUILayout.FloatField("生命", definition.maxHealth);
            definition.attack = EditorGUILayout.FloatField("攻击", definition.attack);
            definition.defense = EditorGUILayout.FloatField("防御", definition.defense);
            definition.magicResist = EditorGUILayout.FloatField("法抗", definition.magicResist);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("攻击与部署", EditorStyles.boldLabel);
            definition.attackIntervalTicks = EditorGUILayout.IntField("攻击间隔 tick", definition.attackIntervalTicks);
            definition.windupTicks = EditorGUILayout.IntField("前摇 tick", definition.windupTicks);
            definition.useProjectile = EditorGUILayout.Toggle("普通攻击使用弹道", definition.useProjectile);
            if (definition.useProjectile)
            {
                definition.projectileSpeed = EditorGUILayout.FloatField(
                    "弹道速度",
                    definition.projectileSpeed);
            }

            definition.attackHitNormalizedTime = EditorGUILayout.Slider(
                definition.useProjectile ? "弹道发射时机" : "普通攻击伤害时机",
                definition.attackHitNormalizedTime,
                0f,
                1f);
            definition.blockCount = EditorGUILayout.IntField("阻挡数量", definition.blockCount);
            definition.deployCost = EditorGUILayout.IntField("部署费用", definition.deployCost);
            definition.canDeployOnHighGround = EditorGUILayout.Toggle("可部署高台", definition.canDeployOnHighGround);
            definition.requiresHighGround = EditorGUILayout.Toggle("只能部署高台", definition.requiresHighGround);
            if (definition.requiresHighGround)
            {
                definition.canDeployOnHighGround = true;
            }
            definition.isHealer = EditorGUILayout.Toggle("治疗职业", definition.isHealer);
            definition.targetSelector = (int)(TargetSelectorType)EditorGUILayout.EnumPopup(
                "索敌策略",
                (TargetSelectorType)definition.targetSelector);
            definition.damageType = (int)(DamageType)EditorGUILayout.EnumPopup(
                "伤害类型",
                (DamageType)definition.damageType);
            definition.baseDamage = EditorGUILayout.FloatField("固定伤害", definition.baseDamage);
            definition.attackScale = EditorGUILayout.FloatField("攻击倍率", definition.attackScale);

            EditorGUILayout.Space(6f);
            DrawAttackRange(definition);

            EditorGUILayout.Space(6f);
            DrawSkills(definition);

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(BuildPreview(definition), MessageType.None);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawAttackRange(OperatorDefinition definition)
        {
            EditorGUILayout.LabelField("攻击范围（相对坐标，朝向由部署旋转）", EditorStyles.boldLabel);
            if (definition.rangeCells == null)
            {
                definition.rangeCells = new List<GridPoint>();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清空范围"))
            {
                definition.rangeCells.Clear();
            }

            if (GUILayout.Button("近战：自身+前一格"))
            {
                SetRange(definition, new[] { new GridPoint(0, 0), new GridPoint(1, 0) });
            }

            if (GUILayout.Button("远程：前方三格"))
            {
                SetRange(definition, new[]
                {
                    new GridPoint(1, 0),
                    new GridPoint(2, 0),
                    new GridPoint(3, 0)
                });
            }

            EditorGUILayout.EndHorizontal();

            for (int y = RangeRadiusY; y >= -RangeRadiusY; y--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = -RangeRadiusX; x <= RangeRadiusX; x++)
                {
                    var point = new GridPoint(x, y);
                    bool selected = Contains(definition.rangeCells, point);
                    Color oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = selected
                        ? new Color(0.3f, 0.9f, 0.5f)
                        : (x == 0 && y == 0 ? new Color(0.75f, 0.75f, 0.75f) : Color.white);

                    if (GUILayout.Button(selected ? "■" : "·", GUILayout.Width(28f), GUILayout.Height(28f)))
                    {
                        Toggle(definition.rangeCells, point);
                    }

                    GUI.backgroundColor = oldColor;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSkills(OperatorDefinition definition)
        {
            EditorGUILayout.LabelField("技能绑定（每局使用第一个）", EditorStyles.boldLabel);
            if (definition.skillIds == null)
            {
                definition.skillIds = new List<string>();
            }

            string[] labels = BuildSkillLabels();
            for (int i = 0; i < definition.skillIds.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                int current = FindSkillIndex(definition.skillIds[i]);
                int next = EditorGUILayout.Popup("技能 " + (i + 1), current, labels);
                if (next >= 0 && next < labels.Length && _skillLibrary != null && _skillLibrary.skills != null
                    && next < _skillLibrary.skills.Count)
                {
                    definition.skillIds[i] = _skillLibrary.skills[next].id;
                }

                if (GUILayout.Button("移除", GUILayout.Width(56f)))
                {
                    definition.skillIds.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("添加技能槽"))
            {
                definition.skillIds.Add(_skillLibrary != null && _skillLibrary.skills != null
                    && _skillLibrary.skills.Count > 0
                    ? _skillLibrary.skills[0].id
                    : string.Empty);
            }
        }

        private string[] BuildSkillLabels()
        {
            if (_skillLibrary == null || _skillLibrary.skills == null || _skillLibrary.skills.Count == 0)
            {
                return new[] { "(没有技能配置)" };
            }

            var labels = new string[_skillLibrary.skills.Count];
            for (int i = 0; i < _skillLibrary.skills.Count; i++)
            {
                SkillDefinition skill = _skillLibrary.skills[i];
                labels[i] = skill != null ? skill.displayName + " (" + skill.id + ")" : "(空)";
            }

            return labels;
        }

        private int FindSkillIndex(string skillId)
        {
            if (_skillLibrary == null || _skillLibrary.skills == null)
            {
                return 0;
            }

            for (int i = 0; i < _skillLibrary.skills.Count; i++)
            {
                if (_skillLibrary.skills[i] != null && _skillLibrary.skills[i].id == skillId)
                {
                    return i;
                }
            }

            return 0;
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private OperatorDefinition GetSelected()
        {
            if (_library == null || _library.operators == null)
            {
                return null;
            }

            if (_selectedIndex < 0 || _selectedIndex >= _library.operators.Count)
            {
                return null;
            }

            return _library.operators[_selectedIndex];
        }

        private void Load()
        {
            _library = LoadLibrary<OperatorLibrary>(OperatorPath);
            if (_library == null)
            {
                _library = new OperatorLibrary();
            }

            if (_library.operators == null)
            {
                _library.operators = new List<OperatorDefinition>();
            }

            _skillLibrary = LoadLibrary<SkillLibrary>(SkillPath);
            if (_skillLibrary == null)
            {
                _skillLibrary = new SkillLibrary();
            }

            if (_skillLibrary.skills == null)
            {
                _skillLibrary.skills = new List<SkillDefinition>();
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.operators.Count - 1);
            _status = "已读取 " + OperatorPath;
        }

        private static T LoadLibrary<T>(string assetPath) where T : class
        {
            string absolutePath = Path.Combine(Application.dataPath, assetPath.Substring("Assets/".Length));
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
                Debug.LogError("读取配置失败：" + assetPath + "\n" + exception.Message);
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
                OperatorPath.Substring("Assets/".Length));
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(_library, true));
            AssetDatabase.Refresh();
            _status = "已保存 " + OperatorPath + "，重新 Play 后生效。";
        }

        private bool Validate(out string error)
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < _library.operators.Count; i++)
            {
                OperatorDefinition definition = _library.operators[i];
                if (definition == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(definition.id))
                {
                    error = $"第 {i + 1} 个角色没有 ID。";
                    return false;
                }

                if (!ids.Add(definition.id))
                {
                    error = "角色 ID 重复：" + definition.id;
                    return false;
                }

                if (definition.maxHealth <= 0f)
                {
                    error = definition.id + " 的生命必须大于 0。";
                    return false;
                }

                if (definition.attackIntervalTicks <= 0)
                {
                    error = definition.id + " 的攻击间隔必须大于 0。";
                    return false;
                }

                if (definition.windupTicks > definition.attackIntervalTicks)
                {
                    error = definition.id + " 的前摇不能大于攻击间隔。";
                    return false;
                }

                if (definition.rangeCells == null || definition.rangeCells.Count == 0)
                {
                    error = definition.id + " 至少需要一个攻击范围格子。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void AddOperator()
        {
            string id = "operator_new";
            int suffix = 1;
            while (FindOperator(id) != null)
            {
                id = "operator_new_" + suffix;
                suffix++;
            }

            _library.operators.Add(new OperatorDefinition
            {
                id = id,
                displayName = "新角色",
                maxHealth = 100f,
                attack = 20f,
                attackIntervalTicks = 30,
                windupTicks = 9,
                blockCount = 1,
                deployCost = 5,
                rangeCells = new List<GridPoint>
                {
                    new GridPoint(0, 0),
                    new GridPoint(1, 0)
                }
            });
            _selectedIndex = _library.operators.Count - 1;
        }

        private void DuplicateOperator()
        {
            OperatorDefinition source = GetSelected();
            if (source == null)
            {
                return;
            }

            OperatorDefinition copy = JsonUtility.FromJson<OperatorDefinition>(JsonUtility.ToJson(source));
            copy.id = source.id + "_copy";
            copy.displayName = source.displayName + " 副本";
            _library.operators.Add(copy);
            _selectedIndex = _library.operators.Count - 1;
        }

        private void DeleteOperator()
        {
            if (GetSelected() == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("删除角色", "确定删除这个角色配置吗？", "删除", "取消"))
            {
                return;
            }

            _library.operators.RemoveAt(_selectedIndex);
            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.operators.Count - 1);
        }

        private OperatorDefinition FindOperator(string id)
        {
            for (int i = 0; i < _library.operators.Count; i++)
            {
                if (_library.operators[i] != null && _library.operators[i].id == id)
                {
                    return _library.operators[i];
                }
            }

            return null;
        }

        private static string BuildPreview(OperatorDefinition definition)
        {
            float seconds = definition.attackIntervalTicks / (float)CombatConstants.TickRate;
            return $"部署费用 {definition.deployCost}    阻挡 {definition.blockCount}\n" +
                   $"生命 {definition.maxHealth:0}  攻击 {definition.attack:0}  " +
                   $"防御 {definition.defense:0}  法抗 {definition.magicResist:0}\n" +
                   $"攻击间隔 {seconds:0.00} 秒，前摇 {definition.windupTicks} tick\n" +
                   $"范围格子 {definition.rangeCells?.Count ?? 0} 个";
        }

        private static void SetRange(OperatorDefinition definition, GridPoint[] points)
        {
            definition.rangeCells.Clear();
            for (int i = 0; i < points.Length; i++)
            {
                definition.rangeCells.Add(points[i]);
            }
        }

        private static bool Contains(List<GridPoint> points, GridPoint target)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].x == target.x && points[i].y == target.y)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Toggle(List<GridPoint> points, GridPoint target)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].x == target.x && points[i].y == target.y)
                {
                    points.RemoveAt(i);
                    return;
                }
            }

            points.Add(target);
        }
    }
}
