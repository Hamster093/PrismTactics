/****************************************************
    文件：SkillEditorWindow.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 01:35:00
	功能：技能配置编辑器：技能列表、SP/效果字段、范围点编辑、校验与数值预览。
*****************************************************/

using System.Collections.Generic;
using System.IO;
using PrismTactics.Battle;
using PrismTactics.Combat;
using UnityEditor;
using UnityEngine;

namespace PrismTactics.EditorTools
{
    public sealed class SkillEditorWindow : EditorWindow
    {
        private const string SkillPath = "Assets/GameContent/Configs/Skills/skills.json";

        private SkillLibrary _library;
        private int _selectedIndex = -1;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _status = string.Empty;

        [MenuItem("Tools/PrismTactics/1 内容编辑/1.1 角色与技能/1.1.2 技能编辑器")]
        public static void Open()
        {
            var window = GetWindow<SkillEditorWindow>("PrismTactics 技能编辑器");
            window.minSize = new Vector2(820f, 620f);
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

            if (GUILayout.Button("新增技能", EditorStyles.toolbarButton))
            {
                AddSkill();
            }

            if (GUILayout.Button("复制技能", EditorStyles.toolbarButton))
            {
                DuplicateSkill();
            }

            if (GUILayout.Button("删除技能", EditorStyles.toolbarButton))
            {
                DeleteSkill();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(SkillPath, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(230f));
            EditorGUILayout.LabelField("技能列表", EditorStyles.boldLabel);
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

            if (_library.skills != null)
            {
                for (int i = 0; i < _library.skills.Count; i++)
                {
                    SkillDefinition skill = _library.skills[i];
                    string label = skill != null && !string.IsNullOrEmpty(skill.displayName)
                        ? skill.displayName
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
            SkillDefinition skill = GetSelected();
            if (skill == null)
            {
                EditorGUILayout.HelpBox("左侧选择一个技能，或点击“新增技能”。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            EditorGUILayout.LabelField("基础信息", EditorStyles.boldLabel);
            skill.id = EditorGUILayout.TextField("技能 ID", skill.id);
            skill.displayName = EditorGUILayout.TextField("显示名称", skill.displayName);
            EditorGUILayout.LabelField("描述");
            skill.description = EditorGUILayout.TextArea(skill.description, GUILayout.Height(56f));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("SP 与触发", EditorStyles.boldLabel);
            skill.activationType = (int)(SkillActivationType)EditorGUILayout.EnumPopup(
                "触发方式",
                (SkillActivationType)skill.activationType);
            skill.spRecoveryType = (int)(SpRecoveryType)EditorGUILayout.EnumPopup(
                "SP 回复",
                (SpRecoveryType)skill.spRecoveryType);
            skill.spCost = EditorGUILayout.IntField("SP 消耗", skill.spCost);
            skill.initialSp = EditorGUILayout.IntField("初始 SP", skill.initialSp);
            skill.spPerSecond = EditorGUILayout.FloatField("每秒 SP", skill.spPerSecond);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("效果", EditorStyles.boldLabel);
            skill.effectType = (int)(SkillEffectType)EditorGUILayout.EnumPopup(
                "效果类型",
                (SkillEffectType)skill.effectType);
            skill.damageType = (int)(DamageType)EditorGUILayout.EnumPopup(
                "伤害类型",
                (DamageType)skill.damageType);
            skill.damageMultiplier = EditorGUILayout.FloatField("伤害倍率", skill.damageMultiplier);
            skill.hitCount = EditorGUILayout.IntField("伤害次数", skill.hitCount);
            skill.extraHits = EditorGUILayout.IntField("额外攻击次数", skill.extraHits);
            skill.attackPercent = EditorGUILayout.FloatField("攻击力加成", skill.attackPercent);
            skill.attackSpeedPercent = EditorGUILayout.FloatField("攻速加成", skill.attackSpeedPercent);
            skill.attackIntervalReducePercent = EditorGUILayout.Slider(
                "攻击间隔缩短", skill.attackIntervalReducePercent, 0f, 0.95f);
            skill.multiTargetCount = EditorGUILayout.IntSlider(
                "多目标数量（0/1=单体）", skill.multiTargetCount, 0, 6);
            skill.projectileTint = EditorGUILayout.ColorField("弹道颜色", skill.projectileTint);
            skill.healPercent = EditorGUILayout.FloatField("治疗倍率", skill.healPercent);
            skill.attacksAllInRange = EditorGUILayout.Toggle("攻击范围内全体", skill.attacksAllInRange);
            skill.requiresEnemy = EditorGUILayout.Toggle("必须有敌人", skill.requiresEnemy);
            skill.durationTicks = EditorGUILayout.IntField("持续 tick", skill.durationTicks);
            EditorGUILayout.LabelField("持续时间", FormatSeconds(skill.durationTicks));

            EditorGUILayout.Space(6f);
            DrawRange(skill);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("预览", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(BuildPreview(skill), MessageType.None);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRange(SkillDefinition skill)
        {
            EditorGUILayout.LabelField("范围格子（相对坐标，空=普通攻击范围）", EditorStyles.boldLabel);
            if (skill.rangeCells == null)
            {
                skill.rangeCells = new List<GridPoint>();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清空范围"))
            {
                skill.rangeCells.Clear();
            }

            if (GUILayout.Button("前方三格"))
            {
                skill.rangeCells.Clear();
                skill.rangeCells.Add(new GridPoint(1, 0));
                skill.rangeCells.Add(new GridPoint(2, 0));
                skill.rangeCells.Add(new GridPoint(3, 0));
            }

            EditorGUILayout.EndHorizontal();

            // 点格子配置，和角色编辑器的攻击范围一致
            for (int y = RangeRadiusY; y >= -RangeRadiusY; y--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = -RangeRadiusX; x <= RangeRadiusX; x++)
                {
                    var point = new GridPoint(x, y);
                    bool selected = ContainsPoint(skill.rangeCells, point);
                    Color oldColor = GUI.backgroundColor;
                    GUI.backgroundColor = selected
                        ? new Color(0.3f, 0.9f, 0.5f)
                        : (x == 0 && y == 0 ? new Color(0.75f, 0.75f, 0.75f) : Color.white);

                    if (GUILayout.Button(selected ? "■" : "·", GUILayout.Width(28f), GUILayout.Height(28f)))
                    {
                        TogglePoint(skill.rangeCells, point);
                    }

                    GUI.backgroundColor = oldColor;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField("已选格子：" + skill.rangeCells.Count + " 个");
        }

        private const int RangeRadiusX = 4;
        private const int RangeRadiusY = 3;

        private static bool ContainsPoint(List<GridPoint> points, GridPoint point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].x == point.x && points[i].y == point.y)
                {
                    return true;
                }
            }

            return false;
        }

        private static void TogglePoint(List<GridPoint> points, GridPoint point)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].x == point.x && points[i].y == point.y)
                {
                    points.RemoveAt(i);
                    return;
                }
            }

            points.Add(point);
        }

        private void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }
        }

        private SkillDefinition GetSelected()
        {
            if (_library == null || _library.skills == null)
            {
                return null;
            }

            if (_selectedIndex < 0 || _selectedIndex >= _library.skills.Count)
            {
                return null;
            }

            return _library.skills[_selectedIndex];
        }

        private void Load()
        {
            string absolutePath = Path.Combine(
                Application.dataPath,
                "GameContent/Configs/Skills/skills.json");

            if (!File.Exists(absolutePath))
            {
                _library = new SkillLibrary();
                _status = "skills.json 不存在，已创建空配置。";
                return;
            }

            try
            {
                _library = JsonUtility.FromJson<SkillLibrary>(File.ReadAllText(absolutePath));
            }
            catch (System.Exception exception)
            {
                _status = "读取失败：" + exception.Message;
            }

            if (_library == null)
            {
                _library = new SkillLibrary();
            }

            if (_library.skills == null)
            {
                _library.skills = new List<SkillDefinition>();
            }

            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.skills.Count - 1);
            _status = "已读取 " + SkillPath;
        }

        private void Save()
        {
            if (!ValidateSkills(out string error))
            {
                _status = "无法保存：" + error;
                return;
            }

            string absolutePath = Path.Combine(
                Application.dataPath,
                "GameContent/Configs/Skills/skills.json");
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(_library, true));
            AssetDatabase.Refresh();
            _status = "已保存 " + SkillPath;
        }

        private bool ValidateSkills(out string error)
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < _library.skills.Count; i++)
            {
                SkillDefinition skill = _library.skills[i];
                if (skill == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(skill.id))
                {
                    error = $"第 {i + 1} 个技能没有 ID。";
                    return false;
                }

                if (!ids.Add(skill.id))
                {
                    error = "技能 ID 重复：" + skill.id;
                    return false;
                }

                if (skill.spCost <= 0)
                {
                    error = skill.id + " 的 SP 消耗必须大于 0。";
                    return false;
                }

                if (skill.effectType == (int)SkillEffectType.InstantDamage
                    && skill.damageMultiplier <= 0f)
                {
                    error = skill.id + " 的伤害倍率必须大于 0。";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private void AddSkill()
        {
            if (_library == null)
            {
                _library = new SkillLibrary();
            }

            string id = "skill_new";
            int suffix = 1;
            while (FindSkill(id) != null)
            {
                id = "skill_new_" + suffix;
                suffix++;
            }

            _library.skills.Add(new SkillDefinition
            {
                id = id,
                displayName = "新技能",
                description = string.Empty,
                activationType = (int)SkillActivationType.Manual,
                spRecoveryType = (int)SpRecoveryType.Auto,
                spCost = 10,
                initialSp = 0,
                spPerSecond = 1f,
                effectType = (int)SkillEffectType.NextAttack,
                damageMultiplier = 1f,
                hitCount = 1
            });
            _selectedIndex = _library.skills.Count - 1;
        }

        private void DuplicateSkill()
        {
            SkillDefinition source = GetSelected();
            if (source == null)
            {
                return;
            }

            SkillDefinition copy = JsonUtility.FromJson<SkillDefinition>(JsonUtility.ToJson(source));
            copy.id = source.id + "_copy";
            copy.displayName = source.displayName + " 副本";
            _library.skills.Add(copy);
            _selectedIndex = _library.skills.Count - 1;
        }

        private void DeleteSkill()
        {
            if (GetSelected() == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("删除技能", "确定删除这个技能配置吗？", "删除", "取消"))
            {
                return;
            }

            _library.skills.RemoveAt(_selectedIndex);
            _selectedIndex = Mathf.Clamp(_selectedIndex, -1, _library.skills.Count - 1);
        }

        private SkillDefinition FindSkill(string id)
        {
            for (int i = 0; i < _library.skills.Count; i++)
            {
                if (_library.skills[i] != null && _library.skills[i].id == id)
                {
                    return _library.skills[i];
                }
            }

            return null;
        }

        private static string BuildPreview(SkillDefinition skill)
        {
            float attack = 100f;
            float duration = skill.durationTicks / (float)CombatConstants.TickRate;
            float speed = 1f + skill.attackSpeedPercent;

            if (skill.effectType == (int)SkillEffectType.InstantDamage)
            {
                return $"按 100 攻击计算：单次 {attack * skill.damageMultiplier:0} 伤害 × {skill.hitCount} 次\n" +
                       $"目标数：范围内全部敌人";
            }

            if (skill.effectType == (int)SkillEffectType.NextAttack)
            {
                int hits = 1 + skill.extraHits;
                return $"按 100 攻击计算：下一次攻击 {attack * skill.damageMultiplier:0} 伤害 × {hits} 次";
            }

            return $"持续 {duration:0.0} 秒\n" +
                   $"攻击 +{skill.attackPercent * 100f:0}%  攻速 +{skill.attackSpeedPercent * 100f:0}%\n" +
                   $"治疗 {skill.healPercent * 100f:0}% 攻击力\n" +
                   $"每秒攻击次数约 {(CombatConstants.TickRate / 30f) * speed:0.00}";
        }

        private static string FormatSeconds(int ticks)
        {
            if (ticks <= 0)
            {
                return "瞬发";
            }

            return (ticks / (float)CombatConstants.TickRate).ToString("0.0") + " 秒";
        }
    }
}
