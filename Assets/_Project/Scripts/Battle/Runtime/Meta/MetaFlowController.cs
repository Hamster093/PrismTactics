/****************************************************
    文件：MetaFlowController.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/28 17:00:00
	功能：选关、选技能、背包、战斗启动与战后掉落的流程控制。
*****************************************************/

using System.Collections;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PrismTactics.Battle
{
    /// <summary>
    /// Meta 流程控制器：串起"选关 → 选技能 → 战斗 → 战后掉落/结束"的完整局外流程。
    ///
    /// 核心状态流转：
    ///   Start → OpenLevelSelect ──(选关 + StartAction)──► OpenSkillSelect
    ///     ▲                                                    │
    ///     │                                                (StartBattle)
    ///     │                                                    ▼
    ///     │                                            _battleStarted = true
    ///     │                                                    │
    ///     │                                        ┌───────────┴───────────┐
    ///     │                                        ▼                       ▼
    ///     │                                   Victory                  Defeat
    ///     │                                        │                       │
    ///     │                                        ▼                       ▼
    ///     │                              ShowBattleEnd(true)       ShowBattleEnd(false)
    ///     │                                        │                       │
    ///     │                                   ShowDrop                 ReloadScene
    ///     │                                        │
    ///     └──────────────────(ReloadScene / 接受掉落完毕)──────────────┘
    ///
    /// 职责边界：
    /// - 本类只管"流程编排与 UI 绑定"：打开/关闭面板、绑定按钮、切换 HUD；
    /// - 真正的战斗逻辑由 BattleController 负责，本类只监听 BattleState；
    /// - 关卡/遗物/干员/技能数据来自 ContentService 加载的 Library，本类不校验配置合法性；
    /// - 跨场景持久状态（已通关、已选技能、已拥有遗物）由 RunSession 静态类持有。
    ///
    /// 设计取舍：
    /// - 大量使用 FindChild 按名字查找 UI 节点：省去序列化引用，但耦合节点命名，节点改名即静默失效；
    /// - 按钮绑定前统一 RemoveAllListeners：防止重复绑定导致一次点击触发多次；
    /// - Time.timeScale 在菜单/战斗/结束面板之间切换：菜单和结束面板为 0（暂停），战斗为 1。
    /// </summary>
    public sealed class MetaFlowController : MonoBehaviour
    {
        #region 依赖与 UI 面板引用
        /// <summary>UI 管理器；Start 时从同物体获取或动态添加，负责面板的 Open/Close/Get。</summary>
        private UIManager _ui;

        /// <summary>选关面板；Start 时从 UIManager 获取，OpenLevelSelect 中会重新获取。</summary>
        private LevelSelectPanel _levelPanel;

        /// <summary>选技能面板；Start 时从 UIManager 获取。</summary>
        private SkillSelectPanel _skillPanel;

        /// <summary>背包面板；Start 时从 UIManager 获取，ToggleBackpack 中会懒加载兜底。</summary>
        private BackpackPanel _backpackPanel;

        /// <summary>战后掉落面板；ShowDrop 时会重新从 UIManager 获取。</summary>
        private BattleDropPanel _dropPanel;

        /// <summary>战斗结束面板；BindBattleEndPanel 按名字"战斗结束面板"在场景中查找并绑定。</summary>
        private BattleEndPanel _battleEndPanel;

        /// <summary>战斗构建器；StartBattle 时设置地图并调用 Build。</summary>
        private M1BattleBootstrap _bootstrap;

        /// <summary>战斗控制器；Build 之后通过 FindObjectOfType 获取，Update 中监听其 State。</summary>
        private BattleController _battle;

        /// <summary>HUD 根节点；按名字"HUD"查找，菜单模式只显示背包按钮，战斗模式显示全部子节点。</summary>
        private GameObject _hud;

        /// <summary>HUD 上的背包按钮；BindBackpackButton 中绑定 ToggleBackpack。</summary>
        private Button _backpackButton;
        #endregion

        #region 配置库与技能映射
        /// <summary>关卡库；从 "levels" 配置加载，失败时用空库兜底。</summary>
        private LevelLibrary _levels;

        /// <summary>遗物库；从 "relics" 配置加载，失败时用空库兜底。</summary>
        private RelicLibrary _relics;

        /// <summary>干员库；从 BattleConfigIds.Operators 配置加载，失败时用空库兜底。</summary>
        private OperatorLibrary _operators;

        /// <summary>技能库；从 BattleConfigIds.Skills 配置加载，失败时用空库兜底。</summary>
        private SkillLibrary _skills;

        /// <summary>
        /// 技能 id → SkillDefinition 的映射；Start 时由 _skills.skills 构建，
        /// 供 BindSkillButton 按 skillId 快速查显示名。重复 id 后者覆盖前者（未做去重校验）。
        /// </summary>
        private readonly Dictionary<string, SkillDefinition> _skillMap =
            new Dictionary<string, SkillDefinition>();
        #endregion

        #region 临时列表与流程状态
        /// <summary>背包列表项缓存；PopulateBackpack 每次重建时先销毁旧项再清空。</summary>
        private readonly List<GameObject> _backpackItems = new List<GameObject>();

        /// <summary>选关面板中的关卡信息浮层；点击外部时隐藏。</summary>
        private Transform _levelInfo;

        /// <summary>关卡信息浮层中的关卡名文本；SelectLevel 时更新。</summary>
        private Text _levelNameText;

        /// <summary>当前选中的关卡索引；-1 表示未选，GetSelectedLevel 会尝试从 RunSession 恢复。</summary>
        private int _selectedLevel = -1;

        /// <summary>战斗是否已启动；StartBattle 置 true，用于 Update 中跳过菜单逻辑。</summary>
        private bool _battleStarted;

        /// <summary>战斗是否已结束；胜利/失败时置 true，防止 Update 重复触发结束流程。</summary>
        private bool _battleEnded;

        /// <summary>结束面板点击后是否走掉落流程；胜利为 true，失败为 false。</summary>
        private bool _endWillDrop;
        #endregion

        #region 掉落临时状态
        /// <summary>每次胜利掉落的遗物数量；硬编码为 2，未做难度/关卡差异配置。</summary>
        private const int DropCount = 2;

        /// <summary>本次待玩家逐个接受的遗物列表；ShowDrop 时重新抽取，AcceptDrop 逐个消费。</summary>
        private readonly List<RelicDefinition> _pendingDrops = new List<RelicDefinition>();

        /// <summary>掉落卡片实例缓存；首个元素是模板本身，后续按需克隆，避免每局重复实例化。</summary>
        private readonly List<GameObject> _dropCards = new List<GameObject>();

        /// <summary>当前待接受的掉落索引；AcceptDrop 后递增，RefreshDropCards 据此显隐卡片。</summary>
        private int _dropIndex;
        #endregion

        #region Unity 生命周期
        /// <summary>
        /// 启动 Meta 流程。
        ///
        /// 执行顺序：
        /// 1. 校验 ContentService 已加载，否则报错停止；
        /// 2. 加载 levels/relics/operators/skills 四个配置库；
        /// 3. 构建 _skillMap（id → SkillDefinition）；
        /// 4. 兜底 levels/relics 的空列表；
        /// 5. 查找 bootstrap、初始化 UIManager、获取各面板；
        /// 6. 绑定关卡/技能/掉落/结束面板/背包按钮；
        /// 7. 设置 HUD 为菜单模式并打开选关。
        ///
        /// 前置条件：ContentService 已加载；本场景应由 Bootstrap 场景启动。
        /// 注意：_skills.skills 和 _operators.operators 未做 null 检查，直接访问 Count 存在 NRE 风险（事实陈述，未改代码）。
        /// </summary>
        private void Start()
        {
            // 1. 内容未加载时直接停止，避免后续空引用。
            if (!ContentService.IsLoaded)
            {
                Debug.LogError("[Content] 内容尚未加载，Meta 流程停止。请从 Bootstrap 场景启动。");
                return;
            }
            // 2. 加载四个配置库；加载失败用空库兜底，保证后续 Count 访问不 NRE。
            _levels = BattleConfigLoader.Load<LevelLibrary>("levels") ?? new LevelLibrary();
            _relics = BattleConfigLoader.Load<RelicLibrary>("relics") ?? new RelicLibrary();
            _operators = BattleConfigLoader.Load<OperatorLibrary>(BattleConfigIds.Operators)
                         ?? new OperatorLibrary();
            _skills = BattleConfigLoader.Load<SkillLibrary>(BattleConfigIds.Skills)
                      ?? new SkillLibrary();
            // 3. 构建技能映射；注意此处直接访问 _skills.skills.Count，未做 null 兜底。
            for (int i = 0; i < _skills.skills.Count; i++)
            {
                SkillDefinition skill = _skills.skills[i];
                if (skill != null)
                {
                    _skillMap[skill.id] = skill;
                }
            }
            // 4. 兜底 levels/relics 内部列表；operators/skills 的列表未兜底（不对称）。
            if (_levels.levels == null)
            {
                _levels.levels = new List<LevelDefinition>();
            }

            if (_relics.relics == null)
            {
                _relics.relics = new List<RelicDefinition>();
            }
            // 5. 查找战斗构建器，初始化 UI 并获取面板引用。
            _bootstrap = FindObjectOfType<M1BattleBootstrap>();
            _ui = GetComponent<UIManager>() ?? gameObject.AddComponent<UIManager>();
            _ui.ScanPanels();
            _levelPanel = _ui.Get<LevelSelectPanel>();
            _skillPanel = _ui.Get<SkillSelectPanel>();
            _backpackPanel = _ui.Get<BackpackPanel>();
            _dropPanel = _ui.Get<BattleDropPanel>();
            _hud = GameObject.Find("HUD");
            // 6. 绑定各类按钮；绑定前会 RemoveAllListeners，防止重复绑定。
            BindBackpackButton();
            BindLevelButtons();
            BindSkillButtons();
            BindDropButtons();
            BindBattleEndPanel();
            // 7. 菜单态 HUD + 打开选关。
            SetHudForMenu();
            OpenLevelSelect();
        }
        /// <summary>
        /// 每帧处理：
        /// 1. 战斗未开始时，点击选关面板外部关闭关卡信息浮层；
        /// 2. 战斗进行中时，检测 Victory/Defeat 并触发结束流程。
        ///
        /// 注意：胜利分支会 MarkCleared 并暂停 timeScale；失败分支只显示结束面板，不暂停（事实陈述，未改逻辑）。
        /// </summary>
        private void Update()
        {
            // 1. 战斗未开始、选关面板打开、浮层激活、鼠标左键按下：点击浮层外部则隐藏。
            if (!_battleStarted
                && _levelPanel != null
                && _levelPanel.IsOpen
                && _levelInfo != null
                && _levelInfo.gameObject.activeSelf
                && Input.GetMouseButtonDown(0))
            {
                var rect = _levelInfo as RectTransform;
                if (rect == null || !IsPointerInside(rect, Input.mousePosition))
                {
                    _levelInfo.gameObject.SetActive(false);
                }
            }
            // 2. 未进入战斗、已结束或 battle 未就绪时直接返回。
            if (!_battleStarted || _battleEnded || _battle == null)
            {
                return;
            }
            // 3. 胜利：标记已结束、暂停游戏、记录通关、显示结束面板。
            if (_battle.State == BattleState.Victory)
            {
                _battleEnded = true;
                Time.timeScale = 0f;
                LevelDefinition level = GetSelectedLevel();
                if (level != null)
                {
                    RunSession.MarkCleared(level.id);
                }

                ShowBattleEnd(true);
            }
            // 3. 失败：标记已结束并显示失败面板；注意未暂停 timeScale。
            else if (_battle.State == BattleState.Defeat)
            {
                _battleEnded = true;
                ShowBattleEnd(false);
            }
        }
        #endregion

        #region 选关流程
        /// <summary>
        /// 打开选关面板：
        /// - 隐藏结束面板、暂停游戏、HUD 切菜单态；
        /// - 关闭所有面板后重新获取 LevelSelectPanel；
        /// - 查找关卡信息浮层和文本、隐藏浮层；
        /// - 重新绑定关卡按钮，打开面板，显示背包按钮。
        /// 副作用：修改 Time.timeScale=0、_levelPanel/_levelInfo/_levelNameText 引用，触发 UI 绑定。
        /// </summary>
        private void OpenLevelSelect()
        {
            _battleEndPanel?.Hide();
            Time.timeScale = 0f;
            SetHudForMenu();
            _ui.CloseAll();
            _levelPanel = _ui.Get<LevelSelectPanel>();
            _levelInfo = FindChild(_levelPanel.transform, "LevelInfoPanel");
            _levelNameText = FindText(_levelInfo, "LevelNameText");
            if (_levelInfo != null)
            {
                _levelInfo.gameObject.SetActive(false);
            }

            BindLevelButtons();
            _ui.Open<LevelSelectPanel>();
            if (_backpackButton != null)
            {
                _backpackButton.gameObject.SetActive(true);
            }
        }
        /// <summary>
        /// 打开选技能面板；前置条件：已选中关卡，否则直接返回。
        /// 副作用：暂停游戏、关闭所有面板、打开 SkillSelectPanel、重新绑定技能按钮。
        /// </summary>
        private void OpenSkillSelect()
        {
            if (GetSelectedLevel() == null)
            {
                return;
            }

            Time.timeScale = 0f;
            _ui.CloseAll();
            _ui.Open<SkillSelectPanel>();
            BindSkillButtons();
        }
        /// <summary>
        /// 绑定选关面板上的关卡按钮：
        /// 1. 遍历 min(6, levels.Count) 个关卡节点，隐藏多余节点；
        /// 2. 绑定点击到 SelectLevel（闭包捕获 captured 索引）；
        /// 3. 已通关的按钮置灰且不可点；
        /// 4. FitLevelNodes 适配第 6 关的布局缩放；
        /// 5. 绑定开始按钮到 OpenSkillSelect。
        ///
        /// 注意：硬编码 6 个关卡节点名 "LevelNode1"~"LevelNode6"，节点缺失时静默跳过。
        /// </summary>
        private void BindLevelButtons()
        {
            // 1. 最多绑定 6 个关卡节点；6 是硬编码上限。
            int levelCount = Mathf.Min(6, _levels.levels.Count);
            for (int i = 0; i < levelCount; i++)
            {
                Transform node = FindChild(_levelPanel.transform, "LevelNode" + (i + 1));
                if (node == null)
                {
                    continue;
                }

                bool hasLevel = i < _levels.levels.Count;
                node.gameObject.SetActive(hasLevel);
                if (!hasLevel)
                {
                    continue;
                }

                LevelDefinition level = _levels.levels[i];
                Button button = node.GetComponent<Button>();
                int captured = i;
                // 先清空再绑定，防止重复绑定导致一次点击触发多次。
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectLevel(captured));
                // 2. 已通关的关卡置灰且不可点击；颜色硬编码为深灰。
                bool cleared = RunSession.IsCleared(level.id);
                button.interactable = !cleared;
                Image image = node.GetComponent<Image>();
                if (image != null)
                {
                    image.color = cleared
                        ? new Color(0.35f, 0.35f, 0.35f, 1f)
                        : Color.white;
                }
                // 3. 节点内第一个 Text 作为关卡名。
                Text label = node.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = level.displayName;
                }
            }
            // 4. 第 6 关加入后默认布局会贴右边缘，整体缩小一点保证可点击。
            FitLevelNodes(levelCount);
            // 关卡数量不足 6 时，把场景里预置的多余节点隐藏，避免出现“看得见但点不动”的关卡。
            for (int i = levelCount; i < 6; i++)
            {
                Transform extra = FindChild(_levelPanel.transform, "LevelNode" + (i + 1));
                if (extra != null)
                {
                    extra.gameObject.SetActive(false);
                }
            }
            // 5. 开始按钮绑定到选技能。
            Transform start = FindChild(_levelPanel.transform, "StartActionButton");
            if (start != null)
            {
                Button button = start.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OpenSkillSelect);
            }
        }
        /// <summary>
        /// 按关卡数量调整关卡节点根节点的缩放：超过 5 关时缩到 0.85，否则保持 1。
        /// 硬编码阈值 5 和缩放 0.85 是为第 6 关布局兜底，未做自适应。
        /// </summary>
        private void FitLevelNodes(int levelCount)
        {
            Transform nodes = FindChild(_levelPanel.transform, "LevelNodes");
            if (nodes == null)
            {
                return;
            }

            // 第 6 关加入后，默认布局会贴到右侧边缘；整体缩小一点保证可点击。
            float scale = levelCount > 5 ? 0.85f : 1f;
            nodes.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// 选中关卡：更新 _selectedLevel 并写入 RunSession，显示关卡信息浮层。
        /// 前置条件：索引合法且该关卡未通关，否则直接返回。
        /// 副作用：修改 _selectedLevel、RunSession.SelectedLevelIndex/SelectedLevelId、浮层与文本。
        /// </summary>
        private void SelectLevel(int index)
        {
            // 索引越界或已通关时忽略点击。
            if (index < 0 || index >= _levels.levels.Count)
            {
                return;
            }

            if (RunSession.IsCleared(_levels.levels[index].id))
            {
                return;
            }

            _selectedLevel = index;
            RunSession.SelectedLevelIndex = index;
            RunSession.SelectedLevelId = _levels.levels[index].id;
            if (_levelInfo != null)
            {
                _levelInfo.gameObject.SetActive(true);
            }

            if (_levelNameText != null)
            {
                _levelNameText.text = _levels.levels[index].displayName;
            }
        }
        #endregion

        #region 选技能流程
        /// <summary>
        /// 绑定选技能面板：
        /// 1. 遍历 12 个干员卡片槽位（硬编码 12）；
        /// 2. 无干员的槽位显示空图并禁用按钮；
        /// 3. 有干员的槽位填充名称、头像，并绑定两个技能按钮；
        /// 4. 绑定开始战斗和返回按钮。
        ///
        /// 注意：_operators.operators 未做 null 检查，直接访问 Count 存在 NRE 风险（事实陈述，未改代码）。
        /// </summary>
        private void BindSkillButtons()
        {
            if (_skillPanel == null)
            {
                return;
            }

            Transform grid = FindChild(_skillPanel.transform, "OperatorGrid");
            if (grid == null)
            {
                return;
            }
            // 1. 12 是硬编码的干员槽位上限。
            for (int i = 0; i < 12; i++)
            {
                Transform card = FindChild(grid, "OperatorCard" + (i + 1));
                if (card == null)
                {
                    continue;
                }

                bool hasOperator = i < _operators.operators.Count;
                // 无论是否有干员都先激活卡片，空槽通过内容隐藏和按钮禁用来表现。
                card.gameObject.SetActive(true);
                Transform filled = FindChild(card, "FilledContent");
                Image background = card.GetComponent<Image>();
                Button cardButton = card.GetComponent<Button>();
                // 2. 空槽：隐藏填充内容、换空图、禁用按钮。
                if (!hasOperator)
                {
                    if (filled != null)
                    {
                        filled.gameObject.SetActive(false);
                    }

                    if (background != null)
                    {
                        // 空槽图标路径硬编码；优先从 UI 图集取，图集未命中时回退单图。
                        background.sprite = UiAtlasService.GetByPath(
                            "UIHandDrawn/Meta/ui_operator_slot_empty");
                        background.color = Color.white;
                    }

                    if (cardButton != null)
                    {
                        cardButton.interactable = false;
                    }

                    continue;
                }
                // 3.有干员：显示填充内容、换选中底图、启用按钮、填名称 / 头像 / 技能。
                OperatorDefinition definition = _operators.operators[i];
                if (filled != null)
                {
                    filled.gameObject.SetActive(true);
                }

                if (background != null)
                {
                    background.sprite = UiAtlasService.GetByPath(
                        "UIHandDrawn/Meta/ui_operator_card_selected");
                    background.color = Color.white;
                }

                if (cardButton != null)
                {
                    cardButton.interactable = true;
                }

                SetText(card, "OperatorNameText", definition.displayName);
                SetPortrait(card, definition.id);
                BindSkillButton(filled, definition, 1);
                BindSkillButton(filled, definition, 2);
            }
            // 4. 开始战斗和返回按钮。
            Transform start = FindChild(_skillPanel.transform, "StartBattleButton");
            if (start != null)
            {
                Button button = start.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(StartBattle);
            }

            Transform back = FindChild(_skillPanel.transform, "BackButton");
            if (back != null)
            {
                Button button = back.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(OpenLevelSelect);
            }
        }
        /// <summary>
        /// 绑定单个干员的第 skillNumber 个技能按钮（1/2）。
        /// 前置条件：filled 非空、技能按钮节点存在、definition.skillIds 有对应索引；否则隐藏按钮。
        /// 副作用：设置按钮文字、绑定点击到 SelectSkill、刷新视觉。
        /// 注意：skillNumber 是 1-based，内部转 0-based；skillId 在 _skillMap 找不到时显示原始 id。
        /// </summary>
        private void BindSkillButton(Transform filled, OperatorDefinition definition, int skillNumber)
        {
            if (filled == null)
            {
                return;
            }

            Transform skillButtonTransform = FindChild(filled, "SkillButton" + skillNumber);
            if (skillButtonTransform == null)
            {
                return;
            }

            int index = skillNumber - 1;
            bool hasSkill = definition.skillIds != null && index < definition.skillIds.Count;
            skillButtonTransform.gameObject.SetActive(hasSkill);
            if (!hasSkill)
            {
                return;
            }

            string skillId = definition.skillIds[index];
            SkillDefinition skill;
            // 找不到定义时退化为显示 skillId，便于排查配置缺失。
            _skillMap.TryGetValue(skillId, out skill);
            Button button = skillButtonTransform.GetComponent<Button>();
            Text label = skillButtonTransform.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = skill != null ? skill.displayName : skillId;
            }

            // 保留 SkillButton 预制体原本的白色底图，不再覆盖技能图标。
            int capturedIndex = index;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SelectSkill(definition, filled, capturedIndex));
            RefreshSkillButtonVisual(filled, definition, skillNumber);
        }
        /// <summary>
        /// 选择干员的技能：写入 RunSession 并刷新该干员两个技能按钮的视觉。
        /// 副作用：修改 RunSession 中该干员的选中技能索引。
        /// </summary>
        private void SelectSkill(OperatorDefinition definition, Transform filled, int index)
        {
            RunSession.SetSelectedSkillIndex(definition.id, index);
            RefreshSkillButtonVisual(filled, definition, 1);
            RefreshSkillButtonVisual(filled, definition, 2);
        }
        /// <summary>
        /// 刷新技能按钮视觉：选中为白色，未选中为深灰。
        /// 颜色值与选关置灰保持一致（0.35 灰）。
        /// </summary>
        private void RefreshSkillButtonVisual(
            Transform filled,
            OperatorDefinition definition,
            int skillNumber)
        {
            Transform skillButtonTransform = FindChild(filled, "SkillButton" + skillNumber);
            if (skillButtonTransform == null)
            {
                return;
            }

            int index = skillNumber - 1;
            bool selected = RunSession.GetSelectedSkillIndex(definition.id) == index;
            Image image = skillButtonTransform.GetComponent<Image>();
            if (image != null)
            {
                image.color = selected
                    ? Color.white
                    : new Color(0.35f, 0.35f, 0.35f, 1f);
            }
        }
        /// <summary>
        /// 设置干员头像：通过 OperatorPortraitUtility 加载裁剪后的 Sprite，保持宽高比。
        /// 注意：局部变量 portrait 赋值后未使用，可能是历史遗留（事实陈述，未改代码）。
        /// </summary>
        private void SetPortrait(Transform card, string operatorId)
        {
            string portrait = GetPortraitName(operatorId);
            Transform portraitTransform = FindChild(card, "OperatorPortrait");
            if (portraitTransform == null)
            {
                return;
            }

            Image image = portraitTransform.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = OperatorPortraitUtility.LoadCropped(operatorId);
                image.preserveAspect = true;
                image.color = Color.white;
            }
        }
        /// <summary>
        /// 头像名工具包装：当前直接转发 OperatorPortraitUtility.GetPortraitName，保留独立方法可能是为后续扩展。
        /// </summary>
        private static string GetPortraitName(string operatorId)
        {
            return OperatorPortraitUtility.GetPortraitName(operatorId);
        }
        #endregion

        #region 战斗启动与结束
        /// <summary>
        /// 启动战斗：
        /// 1. 校验已选关卡和 bootstrap 存在；
        /// 2. 恢复 timeScale、隐藏结束面板、关闭所有 UI、HUD 切战斗态；
        /// 3. 设置地图并 Build；
        /// 4. 查找 BattleController 并标记战斗开始。
        ///
        /// 注意：_battle = FindObjectOfType 假设 Build 后场景只有一个 BattleController；
        ///       Build 是同步的，若未来改为异步需调整查找时机。
        /// </summary>
        private void StartBattle()
        {
            LevelDefinition level = GetSelectedLevel();
            if (level == null || _bootstrap == null)
            {
                return;
            }

            Time.timeScale = 1f;
            _battleEndPanel?.Hide();
            _ui.CloseAll();
            SetHudForBattle();
            _bootstrap.SetMapId(level.mapId);
            _bootstrap.Build();
            _battle = FindObjectOfType<BattleController>();
            _battleStarted = true;
            _battleEnded = false;
        }

        /// <summary>
        /// 在场景中查找名为"战斗结束面板"的节点并绑定结束事件。
        /// 使用 FindObjectsOfType<Transform>(true) 包含 inactive 对象，全场景遍历一次。
        /// 注意：Clicked 事件订阅后未在 OnDestroy 取消订阅，依赖场景重载销毁对象（事实陈述，未改代码）。
        /// </summary>
        private void BindBattleEndPanel()
        {
            Transform[] all = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "战斗结束面板")
                {
                    GameObject panelObject = all[i].gameObject;
                    _battleEndPanel = panelObject.GetComponent<BattleEndPanel>();
                    if (_battleEndPanel == null)
                    {
                        _battleEndPanel = panelObject.AddComponent<BattleEndPanel>();
                    }

                    _battleEndPanel.Clicked += HandleBattleEndClicked;
                    _battleEndPanel.Hide();
                    return;
                }
            }
        }
        /// <summary>
        /// 显示战斗结束面板：
        /// - 记录 _endWillDrop（胜利才掉落）；
        /// - 清理伤害飘字、暂停游戏、隐藏 HUD；
        /// - 有结束面板则显示，否则回退到旧流程（胜利走掉落，失败重载场景）。
        /// 副作用：修改 timeScale、_endWillDrop、HUD 激活状态。
        /// </summary>
        private void ShowBattleEnd(bool victory)
        {
            _endWillDrop = victory;
            DamageNumberManager.ClearAll();
            Time.timeScale = 0f;

            if (_hud != null)
            {
                _hud.SetActive(false);
            }

            if (_battleEndPanel != null)
            {
                _battleEndPanel.Show(victory);
                return;
            }

            // 场景里没有结束面板时保留旧流程，避免卡死。
            if (victory)
            {
                ShowDrop();
            }
            else
            {
                ReloadScene();
            }
        }
        /// <summary>
        /// 结束面板点击回调：隐藏面板后按 _endWillDrop 走掉落或重载场景。
        /// 前置条件：_battleEnded 为 true，否则忽略（防御性检查，正常时序下点击时已为 true）。
        /// </summary>
        private void HandleBattleEndClicked()
        {
            if (!_battleEnded)
            {
                return;
            }

            if (_battleEndPanel != null)
            {
                _battleEndPanel.Hide();
            }

            if (_endWillDrop)
            {
                ShowDrop();
            }
            else
            {
                ReloadScene();
            }
        }
        #endregion

        #region 战后掉落
        /// <summary>
        /// 显示战后掉落：
        /// 1. 清空待掉落列表并抽取 DropCount 个遗物；
        /// 2. 无可用掉落时直接重载场景；
        /// 3. 记录第一个遗物 id 到 RunSession.PendingRelicId；
        /// 4. 打开掉落面板、重置索引、设置卡片、绑定按钮；
        /// 5. 隐藏 HUD。
        /// 副作用：修改 _pendingDrops、_dropIndex、RunSession.PendingRelicId、UI 状态。
        /// </summary>
        private void ShowDrop()
        {
            _pendingDrops.Clear();
            PickDrops(DropCount);
            if (_pendingDrops.Count == 0)
            {
                ReloadScene();
                return;
            }
            // 先记录第一个，AcceptDrop 会随索引推进更新。
            RunSession.PendingRelicId = _pendingDrops[0].id;
            _dropPanel = _ui.Get<BattleDropPanel>();
            _ui.Open<BattleDropPanel>();
            _dropIndex = 0;
            SetDropCards();
            BindDropButtons();
            if (_hud != null)
            {
                _hud.SetActive(false);
            }
        }
        /// <summary>
        /// 从"尚未拥有"的遗物中随机抽取 count 个，去重后写入 _pendingDrops。
        /// 注意：Mathf.Max(1, count) 保证至少尝试抽 1 个；available 为空时 target 为 0，不抽。
        /// 副作用：修改 _pendingDrops（追加，调用前需 Clear）。
        /// </summary>
        private void PickDrops(int count)
        {
            var available = new List<RelicDefinition>();
            for (int i = 0; i < _relics.relics.Count; i++)
            {
                RelicDefinition relic = _relics.relics[i];
                if (relic != null && !RunSession.HasRelic(relic.id))
                {
                    available.Add(relic);
                }
            }
            // 至少抽 1 个，但不超过可用的数量。
            int target = Mathf.Min(Mathf.Max(1, count), available.Count);
            for (int i = 0; i < target; i++)
            {
                int index = Random.Range(0, available.Count);
                _pendingDrops.Add(available[index]);
                available.RemoveAt(index);
            }
        }
        /// <summary>
        /// 接受当前掉落：加入 RunSession、索引递增、更新 PendingRelicId、刷新卡片显隐。
        /// 前置条件：_dropIndex 未越界，否则直接返回。
        /// 副作用：修改 RunSession 遗物列表和 PendingRelicId、_dropIndex、卡片激活状态。
        /// </summary>
        private void AcceptDrop()
        {
            if (_dropIndex >= _pendingDrops.Count)
            {
                return;
            }

            RelicDefinition relic = _pendingDrops[_dropIndex];
            RunSession.AddRelic(relic.id);
            _dropIndex++;
            RunSession.PendingRelicId = _dropIndex < _pendingDrops.Count
                ? _pendingDrops[_dropIndex].id
                : string.Empty;

            RefreshDropCards();
        }
        /// <summary>
        /// 构建掉落卡片：
        /// - 首次调用把模板本身加入 _dropCards 作为第一张卡；
        /// - 后续按需克隆模板到 content，命名 "RelicDropCard_N"；
        /// - 遍历所有卡片，按 _pendingDrops 填充图标/名称/描述，多余的隐藏。
        /// 前置条件：DropContent 和 RelicDropCard 模板存在，否则直接返回。
        /// 注意：模板本身被复用为第一张卡，之后 SetActive 会控制其显隐；模板未在场景中隐藏的话首帧会闪一下（事实陈述，未改代码）。
        /// </summary>
        private void SetDropCards()
        {
            Transform content = _dropPanel != null
                ? FindChild(_dropPanel.transform, "DropContent")
                : null;
            Transform template = content != null ? FindChild(content, "RelicDropCard") : null;
            if (template == null)
            {
                return;
            }
            // 首次调用：模板本身作为第一张卡加入缓存。
            if (_dropCards.Count == 0)
            {
                _dropCards.Add(template.gameObject);
            }
            // 卡片数量不足时克隆模板补齐。
            while (_dropCards.Count < _pendingDrops.Count)
            {
                GameObject clone = Object.Instantiate(template.gameObject, content);
                clone.name = "RelicDropCard_" + (_dropCards.Count + 1);
                _dropCards.Add(clone);
            }
            // 逐张填充或隐藏。
            for (int i = 0; i < _dropCards.Count; i++)
            {
                GameObject card = _dropCards[i];
                bool hasDrop = i < _pendingDrops.Count;
                card.SetActive(hasDrop);
                if (!hasDrop)
                {
                    continue;
                }

                RelicDefinition relic = _pendingDrops[i];
                Transform cardTransform = card.transform;
                SetImage(cardTransform, "DropRelicIcon", relic.icon);
                SetText(cardTransform, "DropRelicNameText", relic.displayName);
                SetText(cardTransform, "DropRelicDescriptionText", relic.description);

                // 每张卡自己可点：给卡片和它下面所有 Graphic 挂点击转发，点到哪层都算拾取。
                string relicId = relic.id;
                BindCardClick(card, relicId);
                Debug.Log($"[Drop] 卡片 {i} 已绑定点击：{relicId}");
            }
        }

        /// <summary>给卡片及所有子 Graphic 绑定点击拾取；根节点没有 Graphic 时补透明 Image。</summary>
        private void BindCardClick(GameObject card, string relicId)
        {
            Graphic[] graphics = card.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                graphic.raycastTarget = true;
                DropRelicCardClick click = graphic.GetComponent<DropRelicCardClick>();
                if (click == null)
                {
                    click = graphic.gameObject.AddComponent<DropRelicCardClick>();
                }

                click.Initialize(relicId, AcceptDropById);
            }

            if (card.GetComponent<Graphic>() == null)
            {
                Image image = card.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0f);
                image.raycastTarget = true;
                DropRelicCardClick click = card.AddComponent<DropRelicCardClick>();
                click.Initialize(relicId, AcceptDropById);
            }
        }

        /// <summary>按 id 接受指定藏品：从待选列表移除后重建卡片。</summary>
        private void AcceptDropById(string relicId)
        {
            int index = _pendingDrops.FindIndex(relic => relic.id == relicId);
            if (index >= 0)
            {
                AcceptDropAt(index);
            }
        }

        /// <summary>接受指定下标的藏品，并把剩余掉落重新铺到卡片上。</summary>
        private void AcceptDropAt(int index)
        {
            if (index < 0 || index >= _pendingDrops.Count)
            {
                return;
            }

            RelicDefinition relic = _pendingDrops[index];
            RunSession.AddRelic(relic.id);
            _pendingDrops.RemoveAt(index);
            _dropIndex = 0;
            RunSession.PendingRelicId = _pendingDrops.Count > 0 ? _pendingDrops[0].id : string.Empty;
            SetDropCards();
            RefreshDropCards();
        }
        /// <summary>
        /// 按 _dropIndex 刷新卡片显隐：已接受的隐藏，剩余待接受的显示。
        /// </summary>
        private void RefreshDropCards()
        {
            for (int i = 0; i < _dropCards.Count; i++)
            {
                if (_dropCards[i] != null)
                {
                    _dropCards[i].SetActive(i >= _dropIndex && i < _pendingDrops.Count);
                }
            }
        }
        /// <summary>
        /// 绑定掉落面板的接受/退出按钮；接受走 AcceptDrop，退出直接重载场景。
        /// 注意：退出按钮不会把未接受的遗物写入 RunSession，等于放弃剩余掉落（设计取舍，未改逻辑）。
        /// </summary>
        private void BindDropButtons()
        {
            if (_dropPanel == null)
            {
                return;
            }

            Transform accept = FindChild(_dropPanel.transform, "AcceptDropButton");
            if (accept != null)
            {
                Button button = accept.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(AcceptDrop);
            }

            Transform exit = FindChild(_dropPanel.transform, "ExitDropButton");
            if (exit != null)
            {
                Button button = exit.GetComponent<Button>();
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(ReloadScene);
            }
        }
        #endregion

        #region 背包
        /// <summary>
        /// 绑定 HUD 上的 BackpackButton 到 ToggleBackpack。
        /// 前置条件：HUD 存在且子节点名为 "BackpackButton"；否则静默跳过。
        /// </summary>
        private void BindBackpackButton()
        {
            if (_hud == null)
            {
                return;
            }

            Transform buttonTransform = _hud.transform.Find("BackpackButton");
            if (buttonTransform == null)
            {
                return;
            }

            _backpackButton = buttonTransform.GetComponent<Button>();
            if (_backpackButton != null)
            {
                _backpackButton.onClick.RemoveAllListeners();
                _backpackButton.onClick.AddListener(ToggleBackpack);
            }
        }
        /// <summary>
        /// 切换背包面板：已打开则关闭，否则重建列表后打开。
        /// 前置条件：_backpackPanel 非空；为空时尝试从 _ui 懒加载，仍为空则返回。
        /// 副作用：打开时重建背包列表项（销毁旧项、实例化新项）。
        /// </summary>
        private void ToggleBackpack()
        {
            if (_backpackPanel == null)
            {
                _backpackPanel = _ui.Get<BackpackPanel>();
            }

            if (_backpackPanel == null)
            {
                return;
            }

            if (_backpackPanel.IsOpen)
            {
                _ui.Close<BackpackPanel>();
                return;
            }

            PopulateBackpack();
            _ui.Open<BackpackPanel>();
        }
        /// <summary>
        /// 重建背包列表：
        /// 1. 销毁并清空旧列表项；
        /// 2. 查找 RelicGrid 和 RelicItemTemplate；
        /// 3. 隐藏模板，按 RunSession.RelicIds 逐个实例化并填充；
        /// 4. 找不到遗物定义的 id 静默跳过。
        /// 副作用：Destroy 旧项、Instantiate 新项、修改 _backpackItems。
        /// </summary>
        private void PopulateBackpack()
        {
            // 1. 清理旧项。
            for (int i = 0; i < _backpackItems.Count; i++)
            {
                if (_backpackItems[i] != null)
                {
                    Destroy(_backpackItems[i]);
                }
            }

            _backpackItems.Clear();
            // 2. 查找网格和模板。
            Transform grid = FindChild(_backpackPanel.transform, "RelicGrid");
            if (grid == null)
            {
                return;
            }

            Transform template = FindChild(grid, "RelicItemTemplate");
            if (template == null)
            {
                return;
            }
            // 3. 模板隐藏，按遗物 id 逐个实例化。
            template.gameObject.SetActive(false);
            for (int i = 0; i < RunSession.RelicIds.Count; i++)
            {
                RelicDefinition relic = FindRelic(RunSession.RelicIds[i]);
                if (relic == null)
                {
                    continue;
                }

                GameObject item = Instantiate(template.gameObject, grid);
                item.name = "RelicItem_" + relic.id;
                item.SetActive(true);
                _backpackItems.Add(item);
                SetImage(item.transform, "RelicIcon", relic.icon);
                SetText(item.transform, "RelicNameText", relic.displayName);
                SetText(item.transform, "RelicDescriptionText", relic.description);
            }
        }
        /// <summary>
        /// 按 id 线性查找遗物定义；找不到返回 null。
        /// 注意：每次调用都遍历全表，背包项多时有 O(n*m) 开销，但局外 UI 频率低，可接受。
        /// </summary>
        private RelicDefinition FindRelic(string relicId)
        {
            for (int i = 0; i < _relics.relics.Count; i++)
            {
                if (_relics.relics[i] != null && _relics.relics[i].id == relicId)
                {
                    return _relics.relics[i];
                }
            }

            return null;
        }
        #endregion

        #region HUD 控制
        /// <summary>
        /// HUD 切菜单态：显示 HUD、置顶、关闭根背景图，只保留 BackpackButton 子节点。
        /// 注意：子节点名 "BackpackButton" 硬编码，改名会导致按钮消失。
        /// </summary>
        private void SetHudForMenu()
        {
            if (_hud == null)
            {
                return;
            }

            _hud.SetActive(true);
            _hud.transform.SetAsLastSibling();
            // HUD 根节点上的 Background Image 是白色半透明遮罩，菜单态也保持关闭。
            Image background = _hud.GetComponent<Image>();
            if (background != null)
            {
                background.enabled = false;
            }

            for (int i = 0; i < _hud.transform.childCount; i++)
            {
                Transform child = _hud.transform.GetChild(i);
                child.gameObject.SetActive(child.name == "BackpackButton");
            }
        }
        /// <summary>
        /// HUD 切战斗态：显示 HUD 全部子节点，关闭根背景图。
        /// 与 SetHudForMenu 共用背景关闭逻辑，但子节点全开。
        /// </summary>
        private void SetHudForBattle()
        {
            if (_hud == null)
            {
                return;
            }

            _hud.SetActive(true);
            // HUD 根节点上的 Background Image 是白色半透明遮罩，战斗时保持关闭。
            Image background = _hud.GetComponent<Image>();
            if (background != null)
            {
                background.enabled = false;
            }

            for (int i = 0; i < _hud.transform.childCount; i++)
            {
                _hud.transform.GetChild(i).gameObject.SetActive(true);
            }
        }
        #endregion

        #region 查询与工具
        /// <summary>
        /// 获取当前选中的关卡定义。
        /// 若 _selectedLevel 为 -1 但 RunSession 有记录，则从 RunSession 恢复（支持场景重载后继续）。
        /// 返回 null 的情况：索引无效或关卡列表越界。
        /// </summary>
        private LevelDefinition GetSelectedLevel()
        {
            if (_selectedLevel < 0 && RunSession.SelectedLevelIndex >= 0)
            {
                _selectedLevel = RunSession.SelectedLevelIndex;
            }

            if (_selectedLevel < 0 || _selectedLevel >= _levels.levels.Count)
            {
                return null;
            }

            return _levels.levels[_selectedLevel];
        }
        /// <summary>
        /// 判断屏幕坐标是否落在 RectTransform 内。
        /// 注意：Screen Space Overlay 模式下没有 worldCamera，传 null 给 RectangleContainsScreenPoint 是正确的；
        /// 非 Overlay 模式用 canvas.worldCamera。rect 为 null 会抛 NRE，调用方已判空。
        /// </summary>
        private static bool IsPointerInside(RectTransform rect, Vector2 screenPosition)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPosition, camera);
        }
        /// <summary>
        /// 递归查找指定名字的子节点：先查直接子节点，再深度优先递归。
        /// 注意：性能取决于层级深度；大量调用时建议缓存引用。名字不存在返回 null。
        /// </summary>
        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            Transform direct = parent.Find(name);
            if (direct != null)
            {
                return direct;
            }

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = FindChild(parent.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
        /// <summary>查找子节点上的 Text 组件；找不到节点返回 null。</summary>
        private static Text FindText(Transform parent, string name)
        {
            Transform target = FindChild(parent, name);
            return target != null ? target.GetComponent<Text>() : null;
        }
        /// <summary>查找子节点并设置其 Text 文本；节点或组件缺失时静默跳过。</summary>
        private static void SetText(Transform parent, string name, string value)
        {
            Transform target = FindChild(parent, name);
            if (target == null)
            {
                return;
            }

            Text text = target.GetComponent<Text>();
            if (text != null)
            {
                text.text = value;
            }
        }
        /// <summary>
        /// 查找子节点并按资源路径设置 Image 的 Sprite。
        /// 前置条件：resourcePath 非空；节点或 Image 缺失时静默跳过。
        /// 注意：AssetService.Load 的资源路径来自配置（如 relic.icon），路径错误时返回 null 但不报错。
        /// </summary>
        private static void SetImage(Transform parent, string name, string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
            {
                return;
            }

            Transform target = FindChild(parent, name);
            if (target == null)
            {
                return;
            }

            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = UiAtlasService.GetByPath(resourcePath);
                image.color = Color.white;
            }
        }
        /// <summary>
        /// 重载当前场景：先恢复 timeScale 为 1，避免新场景继承暂停状态。
        /// 副作用：销毁当前场景所有对象，RunSession 等静态状态保留。
        /// </summary>
        private static void ReloadScene()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        #endregion
    }
}
