/****************************************************
    文件：BattleHudBinder.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 01:30:00
	功能：把现有 HUD 预制体接入战斗数据：生命、费用、敌人、干员、技能、暂停与倍速。
*****************************************************/

using System;
using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PrismTactics.Battle
{
    public sealed class BattleHudBinder : MonoBehaviour
    {
        private BattleController _controller;
        private BattleDeploymentController _deployment;

        private Text _lifeText;
        private Text _enemyText;
        private Text _costText;
        private Text _remainingDeployText;
        private Text _operatorNameText;
        private Text _attackText;
        private Text _defenseText;
        private Text _resistanceText;
        private Text _blockText;
        private Text _healthText;
        private Slider _healthBar;
        private Text _spText;
        private Text _skillNameText;
        private Text _skillSpTypeText;
        private Text _skillActivationText;
        private Text _skillDescriptionText;

        private Image _portraitImage;
        private Image _pauseImage;
        private Image _speedImage;
        private Image _skillButtonImage;
        private Image _skillInfoIcon;
        private Image _skillCooldownMask;

        private Button _pauseButton;
        private Button _speedButton;
        private Button _settingsButton;
        private Button _retreatButton;
        private Button _skillButton;
        private Button _showSkillRangeButton;

        private GameObject _operatorDetailPanel;
        private GameObject _operatorActionPanel;
        private GameObject _skillRoom;
        private OperatorConfig _previewConfig;
        private RectTransform _actionPanelRect;
        private Vector2 _actionPanelHomePosition;

        private Sprite _pauseSprite;
        private Sprite _resumeSprite;
        private Sprite _speed1xSprite;
        private Sprite _speed2xSprite;
        private Sprite _skillDefaultSprite;

        private bool _paused;
        private bool _speed2x;
        private bool _detailSlowActive;
        // 仅性能对照用：打开后恢复 1.0 的“每帧轮询 UI”行为，默认关闭。
        private bool _legacyPolling;

        // 事件化缓存：只有值变化才刷新文本，避免每帧插值和 UGUI 脏标记。
        private int _lastLife = int.MinValue;
        private int _lastCost = int.MinValue;
        private int _lastRemainingEnemies = int.MinValue;
        private int _lastTotalEnemies = int.MinValue;
        private int _lastUndeployed = int.MinValue;
        private int _lastSp = int.MinValue;
        private bool _lastCanActivate;
        private string _lastSkillId;
        private float _lastAttackValue = float.NaN;
        private int _lastHealthCurrent = int.MinValue;
        private int _lastHealthMax = int.MinValue;
        private CombatEntity _boundAlly;

        // 头像/技能图标按资源路径缓存，战斗内不再每帧 Resources.Load。
        private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

        public event Action SettingsClicked;
        public event Action<CombatEntity> AttackRangeRequested;
        public event Action<CombatEntity> SkillRangeRequested;

        public RectTransform ActionPanelRect => _actionPanelRect;

        public bool InputEnabled { get; set; } = true;

        /// <summary>
        /// 性能 A/B 对照开关：恢复旧版每帧轮询 HUD，仅供 Profiler 前后截图。
        /// 正常游戏保持 false，走事件驱动刷新。
        /// </summary>
        public void SetLegacyPolling(bool enabled)
        {
            _legacyPolling = enabled;
        }

        /// <summary>
        /// 预留给后续“点击干员显示攻击范围”的 UI 调用。
        /// </summary>
        public void RequestAttackRangePreview()
        {
            AttackRangeRequested?.Invoke(_controller != null ? _controller.SelectedAlly : null);
        }

        public void Initialize(BattleController controller, BattleDeploymentController deployment)
        {
            _controller = controller;
            _deployment = deployment;

            GameObject hudRoot = GameObject.Find("HUD");
            Image hudBackground = hudRoot != null ? hudRoot.GetComponent<Image>() : null;
            if (hudBackground != null)
            {
                // 根节点 Background 会形成全屏白色半透明遮罩，HUD 子元素单独渲染即可。
                hudBackground.enabled = false;
            }

            _lifeText = BindText("HUD/EnemyAndLifePanel/LifePanel/LifeText");
            _enemyText = BindText("HUD/EnemyAndLifePanel/EnemyRemainingPanel/EnemyRemainingText");
            _costText = BindText("HUD/CostPanel/CostText");
            _remainingDeployText = BindText("HUD/RemainingDeployPanel/RemainingDeployText");

            _operatorDetailPanel = FindObject("OperatorDetailPanel");
            _operatorActionPanel = FindObject("OperatorActionPanel");
            EnsurePanel<OperatorDetailPanel>(_operatorDetailPanel);
            EnsurePanel<OperatorActionPanel>(_operatorActionPanel);
            _actionPanelRect = _operatorActionPanel != null
                ? _operatorActionPanel.GetComponent<RectTransform>()
                : null;
            if (_actionPanelRect != null)
            {
                _actionPanelHomePosition = _actionPanelRect.anchoredPosition;
            }

            UIManager manager = GetComponent<UIManager>();
            if (manager == null)
            {
                manager = gameObject.AddComponent<UIManager>();
            }

            manager.ScanPanels();

            _operatorNameText = BindText("OperatorDetailPanel/OperatorNameText");
            _portraitImage = BindImage("OperatorDetailPanel/OperatorNameText/OperatorPortrait");
            _attackText = BindText("OperatorDetailPanel/AttackText");
            _defenseText = BindText("OperatorDetailPanel/DefenseText");
            _resistanceText = BindText("OperatorDetailPanel/ResistanceText");
            _blockText = BindText("OperatorDetailPanel/BlockText");
            _healthBar = Find<Slider>("OperatorDetailPanel/HealthBar");
            _healthText = BindText("OperatorDetailPanel/HealthBar/HealthText");

            _skillInfoIcon = BindImage("OperatorDetailPanel/SkillInfoIcon");
            _skillNameText = BindText("OperatorDetailPanel/SkillInfoIcon/SkillNameText");
            _skillSpTypeText = BindText("OperatorDetailPanel/SkillInfoIcon/SkillSpTypeText");
            _skillActivationText = BindText("OperatorDetailPanel/SkillInfoIcon/SkillActivationText");
            _skillDescriptionText = BindText(
                "OperatorDetailPanel/SkillInfoIcon/SkillDescriptionScroll/SkillDescriptionViewport/SkillDescriptionContent/SkillDescriptionText");

            _skillRoom = FindObject("OperatorActionPanel/SkillRoom");
            _spText = BindText("OperatorActionPanel/SkillRoom/SpPanel/SpText");
            _skillButtonImage = BindImage("OperatorActionPanel/SkillRoom/SkillButtonPanel/SkillButton");
            _skillButton = Find<Button>("OperatorActionPanel/SkillRoom/SkillButtonPanel/SkillButton");
            _retreatButton = Find<Button>("OperatorActionPanel/RetreatPanel/RetreatButton");
            _showSkillRangeButton = Find<Button>(
                "OperatorActionPanel/SkillRoom/SkillRangePanel/ShowSkillRangeButton");

            _pauseImage = BindImage("HUD/PausePanel/PauseButton");
            _pauseButton = Find<Button>("HUD/PausePanel/PauseButton");
            _speedImage = BindImage("HUD/SpeedPanel/SpeedButton");
            _speedButton = Find<Button>("HUD/SpeedPanel/SpeedButton");
            _settingsButton = Find<Button>("HUD/SettingsPanel/SettingsButton");

            _pauseSprite = LoadSprite("ui_pause");
            _resumeSprite = LoadSprite("ui_resume");
            _speed1xSprite = LoadSprite("ui_speed_1x");
            _speed2xSprite = LoadSprite("ui_speed_2x");
            // 场景里直接挂的图标也换成图集版本，否则它们仍是独立贴图、不能合批
            ApplyAtlasSprites();
            _skillDefaultSprite = _skillButtonImage != null ? _skillButtonImage.sprite : null;
            _skillCooldownMask = CreateCooldownMask(_skillButtonImage);
            SetSkillRoomVisible(true);

            HookButton(_pauseButton, OnPauseClicked);
            HookButton(_speedButton, OnSpeedClicked);
            HookButton(_settingsButton, OnSettingsClicked);
            HookButton(_retreatButton, OnRetreatClicked);
            HookButton(_skillButton, OnSkillClicked);
            HookButton(_showSkillRangeButton, OnShowSkillRangeClicked);

            ClosePanels();
            SubscribeController();
            RefreshTopBar();
            RefreshPauseAndSpeed();
        }

        private void SubscribeController()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.LifeChanged += RefreshTopBar;
            _controller.CostChanged += RefreshTopBar;
            _controller.EnemiesChanged += RefreshTopBar;
            _controller.AlliesChanged += HandleAlliesChanged;
            _controller.SelectionChanged += HandleSelectionChanged;
        }

        private void UnsubscribeController()
        {
            if (_controller == null)
            {
                return;
            }

            _controller.LifeChanged -= RefreshTopBar;
            _controller.CostChanged -= RefreshTopBar;
            _controller.EnemiesChanged -= RefreshTopBar;
            _controller.AlliesChanged -= HandleAlliesChanged;
            _controller.SelectionChanged -= HandleSelectionChanged;
        }

        private void Update()
        {
            if (!InputEnabled || _controller == null)
            {
                return;
            }

            // 数值刷新已改为事件驱动；每帧只处理鼠标点击。
            if (Input.GetMouseButtonDown(0)
                && _deployment != null
                && _deployment.Selected == null
                && !IsPointerOverUi())
            {
                HandleWorldClick();
            }

            if (_legacyPolling)
            {
                LegacyPoll();
            }
        }

        /// <summary>
        /// 性能对照模式：故意清掉刷新缓存并每帧重新加载图标，复现优化前的 HUD 负载。
        /// 不要把它接到正常游戏流程上。
        /// </summary>
        private void LegacyPoll()
        {
            _lastLife = int.MinValue;
            _lastCost = int.MinValue;
            _lastRemainingEnemies = int.MinValue;
            _lastTotalEnemies = int.MinValue;
            _lastUndeployed = int.MinValue;
            _lastSp = int.MinValue;
            _lastCanActivate = false;
            _lastSkillId = null;
            _spriteCache.Clear();

            RefreshTopBar();
            RefreshPauseAndSpeed();

            CombatEntity selected = _controller.SelectedAlly;
            if (selected != null)
            {
                UpdateAllyRange(selected);
                RefreshOperator(selected);
                RefreshSkill(selected, true);
            }
            else if (_previewConfig != null)
            {
                RefreshPreview(_previewConfig);
            }
        }

        private void HandleWorldClick()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float distance))
            {
                return;
            }

            // 先按立绘的渲染包围盒命中；否则点角色身体会被地面平面判到后面的格子。
            if (TrySelectAllyAtPointer(ray, out CombatEntity clickedAlly))
            {
                _controller.SelectAlly(clickedAlly);
                OpenPanels();
                return;
            }

            Vector3 world = ray.GetPoint(distance);
            Vector2Int cell = _controller.Grid.Map.WorldToCell(world);
            CombatEntity ally = _controller.Grid.GetAllyAt(cell);

            if (ally != null)
            {
                _controller.SelectAlly(ally);
                OpenPanels();
            }
            else
            {
                _controller.SelectAlly(null);
                ClosePanels();
            }
        }

        private bool TrySelectAllyAtPointer(Ray ray, out CombatEntity selected)
        {
            selected = null;
            if (_controller.Allies == null)
            {
                return false;
            }

            float nearest = float.MaxValue;
            for (int i = 0; i < _controller.Allies.Count; i++)
            {
                CombatEntity ally = _controller.Allies[i];
                if (ally == null || !ally.IsAlive)
                {
                    continue;
                }

                Renderer[] renderers = ally.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    Bounds bounds = renderer.bounds;
                    if (!bounds.IntersectRay(ray, out float distance) || distance >= nearest)
                    {
                        continue;
                    }

                    nearest = distance;
                    selected = ally;
                }
            }

            return selected != null;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        public void ShowOperatorPreview(OperatorConfig config)
        {
            _previewConfig = config;
            _controller.SelectAlly(null);
            SetSkillRoomVisible(false);
            HideAllyRanges();
            ResetActionPanelPosition();

            if (UIManager.Instance != null)
            {
                UIManager.Instance.Close<OperatorActionPanel>();
                UIManager.Instance.Open<OperatorDetailPanel>(config);
            }

            BeginDetailSlow();
            RefreshPreview(config);
        }

        public void ShowActionPanelAt(Vector2 screenPosition)
        {
            if (_previewConfig == null || UIManager.Instance == null || _actionPanelRect == null)
            {
                return;
            }

            UIManager.Instance.Open<OperatorActionPanel>(_previewConfig);
            SetActionPanelAtScreenPoint(screenPosition);
        }

        private void SetActionPanelAtScreenPoint(Vector2 screenPosition)
        {
            if (_actionPanelRect == null)
            {
                return;
            }

            Canvas canvas = _actionPanelRect.GetComponentInParent<Canvas>();
            RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
            if (canvasRect == null)
            {
                return;
            }

            Camera uiCamera = canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    canvasRect,
                    screenPosition,
                    uiCamera,
                    out Vector3 worldPoint))
            {
                _actionPanelRect.position = worldPoint;
            }
        }

        private void PositionActionPanelAtSelectedCell()
        {
            if (_controller == null
                || _controller.SelectedAlly == null
                || _actionPanelRect == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector2Int cell = _controller.SelectedAlly.GridPosition;
            Vector3 cellCenter = _controller.Grid.Map.CellToWorld(cell)
                                 + Vector3.up * _controller.Grid.Map.GetSurfaceHeight(cell);
            Vector3 screenPoint = camera.WorldToScreenPoint(cellCenter);
            SetActionPanelAtScreenPoint(screenPoint);
        }

        /// <summary>道具拖拽时进入 0.1 倍速预览：只切慢动作，不打开干员详情面板。</summary>
        public void BeginItemPreview()
        {
            _previewConfig = null;
            SetSkillRoomVisible(false);
            HideAllyRanges();
            ResetActionPanelPosition();
            UIManager.Instance?.CloseAll();
            BeginDetailSlow();
        }

        public void ClearPreview()        {
            _previewConfig = null;
            SetSkillRoomVisible(true);
            HideAllyRanges();
            ResetActionPanelPosition();
            EndDetailSlow();
            UIManager.Instance?.CloseAll();
        }

        public void ResetActionPanelPosition()
        {
            if (_actionPanelRect != null)
            {
                _actionPanelRect.anchoredPosition = _actionPanelHomePosition;
            }
        }

        public void SetSkillRoomVisible(bool visible)
        {
            if (_skillRoom != null)
            {
                _skillRoom.SetActive(visible);
            }
        }

        private void OpenPanels()
        {
            if (UIManager.Instance == null || _controller.SelectedAlly == null)
            {
                return;
            }

            SetSkillRoomVisible(true);
            ResetActionPanelPosition();
            UpdateAllyRange(_controller.SelectedAlly);
            UIManager.Instance.Open<OperatorDetailPanel>(_controller.SelectedAlly);
            UIManager.Instance.Open<OperatorActionPanel>(_controller.SelectedAlly);
            PositionActionPanelAtSelectedCell();
            BeginDetailSlow();
        }

        private void ClosePanels()
        {
            SetSkillRoomVisible(true);
            ResetActionPanelPosition();
            HideAllyRanges();
            EndDetailSlow();
            UIManager.Instance?.CloseAll();
        }

        private void BeginDetailSlow()
        {
            if (_detailSlowActive)
            {
                return;
            }

            _detailSlowActive = true;
            Time.timeScale = 0.1f;
        }

        private void EndDetailSlow()
        {
            if (!_detailSlowActive)
            {
                return;
            }

            _detailSlowActive = false;
            ApplyTimeScale();
        }

        private void HideAllyRanges()
        {
            if (_controller == null || _controller.Allies == null)
            {
                return;
            }

            for (int i = 0; i < _controller.Allies.Count; i++)
            {
                OperatorRangeView view = _controller.Allies[i] != null
                    ? _controller.Allies[i].GetComponent<OperatorRangeView>()
                    : null;
                view?.SetVisible(false);
            }
        }

        private void UpdateAllyRange(CombatEntity selected)
        {
            if (_controller == null || _controller.Allies == null)
            {
                return;
            }

            for (int i = 0; i < _controller.Allies.Count; i++)
            {
                CombatEntity ally = _controller.Allies[i];
                OperatorRangeView view = ally != null ? ally.GetComponent<OperatorRangeView>() : null;
                if (view != null)
                {
                    view.SetVisible(ally == selected);
                }
            }
        }

        /// <summary>
        /// 顶部资源栏：只在数值变化时格式化字符串，避免每帧产生 GC 和 UGUI 重建。
        /// </summary>
        private void RefreshTopBar()
        {
            if (_controller == null)
            {
                return;
            }

            int life = _controller.Life;
            if (life != _lastLife)
            {
                _lastLife = life;
                SetText(_lifeText, life.ToString());
            }

            int total = _controller.TotalEnemies;
            int remaining = _controller.RemainingEnemies;
            if (total != _lastTotalEnemies || remaining != _lastRemainingEnemies)
            {
                _lastTotalEnemies = total;
                _lastRemainingEnemies = remaining;
                SetText(_enemyText, remaining + "/" + total);
            }

            int cost = _controller.Cost;
            if (cost != _lastCost)
            {
                _lastCost = cost;
                SetText(_costText, cost.ToString());
            }

            int undeployed = Mathf.Max(0, _controller.Operators.Count - _controller.Allies.Count);
            if (undeployed != _lastUndeployed)
            {
                _lastUndeployed = undeployed;
                SetText(_remainingDeployText, "剩余可放置角色：" + undeployed);
            }
        }

        private void HandleAlliesChanged()
        {
            RefreshTopBar();

            CombatEntity selected = _controller != null ? _controller.SelectedAlly : null;
            if (selected != null && !selected.IsAlive)
            {
                _controller.SelectAlly(null);
                ClosePanels();
                return;
            }

            if (selected != null)
            {
                RefreshOperator(selected);
                RefreshSkill(selected);
            }
        }

        private void HandleSelectionChanged(CombatEntity ally)
        {
            BindAlly(ally);

            if (ally == null)
            {
                HideAllyRanges();
                if (_previewConfig != null)
                {
                    RefreshPreview(_previewConfig);
                    RefreshPauseAndSpeed();
                }

                return;
            }

            _previewConfig = null;
            UpdateAllyRange(ally);
            RefreshOperator(ally);
            RefreshSkill(ally, true);
        }

        private void BindAlly(CombatEntity ally)
        {
            UnbindAlly();
            _boundAlly = ally;
            _lastAttackValue = float.NaN;
            _lastHealthCurrent = int.MinValue;
            _lastHealthMax = int.MinValue;
            _lastSp = int.MinValue;
            _lastCanActivate = false;
            _lastSkillId = null;

            if (ally == null)
            {
                return;
            }

            if (ally.Health != null)
            {
                ally.Health.HealthChanged += HandleAllyHealthChanged;
            }

            if (ally.Skills != null)
            {
                ally.Skills.SpChanged += HandleAllySpChanged;
            }
        }

        private void UnbindAlly()
        {
            if (_boundAlly == null)
            {
                return;
            }

            if (_boundAlly.Health != null)
            {
                _boundAlly.Health.HealthChanged -= HandleAllyHealthChanged;
            }

            if (_boundAlly.Skills != null)
            {
                _boundAlly.Skills.SpChanged -= HandleAllySpChanged;
            }

            _boundAlly = null;
        }

        private void HandleAllyHealthChanged(float current, float max)
        {
            if (_boundAlly != null)
            {
                // 生命变化只刷新血条和生命文字，不重复拼攻击/防御等属性。
                RefreshOperatorHealth(_boundAlly);
            }
        }

        private void HandleAllySpChanged()
        {
            if (_boundAlly == null)
            {
                return;
            }

            RefreshSkill(_boundAlly);

            // SP 在持续技能期间每 tick 都变，但攻击力只在 Buff 开始/结束时变。
            float attack = _boundAlly.Stats.Get(StatType.Attack);
            if (float.IsNaN(_lastAttackValue) || !Mathf.Approximately(attack, _lastAttackValue))
            {
                _lastAttackValue = attack;
                RefreshOperatorStats(_boundAlly);
            }
        }

        private void RefreshPauseAndSpeed()
        {
            if (_pauseImage != null)
            {
                _pauseImage.sprite = _paused ? _resumeSprite : _pauseSprite;
            }

            if (_speedImage != null)
            {
                _speedImage.sprite = _speed2x ? _speed2xSprite : _speed1xSprite;
            }
        }

        private void RefreshPreview(OperatorConfig config)
        {
            SetText(_operatorNameText, config.displayName);
            SetText(_attackText, "攻击力 " + Mathf.RoundToInt(config.attack));
            SetText(_defenseText, "防御 " + Mathf.RoundToInt(config.defense));
            SetText(_resistanceText, "法抗 " + Mathf.RoundToInt(config.magicResist));
            SetText(_blockText, "阻挡 " + config.blockCount);

            if (_healthBar != null)
            {
                _healthBar.value = 1f;
            }

            SetText(_healthText, $"生命 {config.maxHealth:0}/{config.maxHealth:0}");

            if (_portraitImage != null)
            {
                _portraitImage.sprite = OperatorPortraitUtility.LoadCropped(config.operatorId);
            }

            SkillConfig skill = config.skills != null && config.skills.Length > 0
                ? config.skills[0]
                : null;

            if (skill == null)
            {
                SetSkillEmpty();
                return;
            }

            SetSkillDisplay(skill, 0, skill.MaxSp, false);
        }

        private void RefreshOperator(CombatEntity ally)
        {
            RefreshOperatorStats(ally);
            RefreshOperatorHealth(ally);
        }

        private void RefreshOperatorStats(CombatEntity ally)
        {
            OperatorConfig config = ally.Config as OperatorConfig;
            if (config == null)
            {
                return;
            }

            _lastAttackValue = ally.Stats.Get(StatType.Attack);
            SetText(_operatorNameText, config.displayName);
            SetText(_attackText, "攻击力 " + Mathf.RoundToInt(_lastAttackValue));
            SetText(_defenseText, "防御 " + Mathf.RoundToInt(ally.Stats.Get(StatType.Defense)));
            SetText(_resistanceText, "法抗 " + Mathf.RoundToInt(ally.Stats.Get(StatType.MagicResist)));
            SetText(_blockText, "阻挡 " + ally.BlockCount);

            if (_portraitImage != null)
            {
                // 用统一的立绘映射，避免拼 unit_<数字>_icon 这种不存在的地址。
                _portraitImage.sprite = OperatorPortraitUtility.LoadCropped(config.operatorId);
            }
        }

        private void RefreshOperatorHealth(CombatEntity ally)
        {
            if (_healthBar != null)
            {
                _healthBar.value = ally.MaxHealth > 0f ? ally.CurrentHealth / ally.MaxHealth : 0f;
            }

            int current = Mathf.RoundToInt(ally.CurrentHealth);
            int max = Mathf.RoundToInt(ally.MaxHealth);
            if (current == _lastHealthCurrent && max == _lastHealthMax)
            {
                return;
            }

            _lastHealthCurrent = current;
            _lastHealthMax = max;
            SetText(_healthText, $"生命 {current}/{max}");
        }

        private void RefreshSkill(CombatEntity ally, bool force = false)
        {
            SkillComponent skill = ally.Skills;
            if (skill == null || skill.Config == null)
            {
                SetSkillEmpty();
                return;
            }

            // SP 每 tick 都会变；只在整数 SP、可释放状态或技能 ID 变化时才刷新 UI。
            bool changed = force
                           || skill.CurrentSp != _lastSp
                           || skill.CanActivate != _lastCanActivate
                           || skill.Config.skillId != _lastSkillId;
            if (!changed)
            {
                return;
            }

            _lastSp = skill.CurrentSp;
            _lastCanActivate = skill.CanActivate;
            _lastSkillId = skill.Config.skillId;
            SetSkillDisplay(skill.Config, skill.CurrentSp, skill.MaxSp, skill.CanActivate);
        }

        private void SetSkillEmpty()
        {
            SetText(_spText, "0/0");
            SetText(_skillNameText, "无技能");
            SetText(_skillSpTypeText, string.Empty);
            SetText(_skillActivationText, string.Empty);
            SetText(_skillDescriptionText, string.Empty);

            if (_skillButton != null)
            {
                _skillButton.interactable = false;
            }

            if (_skillCooldownMask != null)
            {
                _skillCooldownMask.gameObject.SetActive(false);
            }
        }

        private void SetSkillDisplay(SkillConfig config, int currentSp, int maxSp, bool canActivate)
        {
            SetText(_spText, $"{currentSp}/{maxSp}");
            SetText(_skillNameText, config.displayName);
            SetText(_skillSpTypeText, GetSpTypeLabel(config.spRecoveryType));
            SetText(_skillActivationText, config.activationType == SkillActivationType.Auto ? "自动触发" : "手动触发");
            SetText(_skillDescriptionText, config.description);

            Sprite skillIcon = LoadSpriteCached("UIHandDrawn/skill_" + config.skillId);
            _skillDefaultSprite = skillIcon;

            if (_skillButtonImage != null)
            {
                _skillButtonImage.sprite = _skillDefaultSprite;
            }

            if (_skillButton != null)
            {
                _skillButton.interactable = canActivate;
            }

            if (_skillInfoIcon != null)
            {
                _skillInfoIcon.sprite = _skillDefaultSprite;
            }

            if (_skillCooldownMask != null)
            {
                float ratio = maxSp > 0 ? (float)currentSp / maxSp : 0f;
                _skillCooldownMask.gameObject.SetActive(!canActivate);
                _skillCooldownMask.fillAmount = 1f - Mathf.Clamp01(ratio);
            }
        }

        private void OnPauseClicked()
        {
            _paused = !_paused;
            ApplyTimeScale();
            // 功能生效后同步按钮图标，否则暂停/继续贴图不会切换。
            RefreshPauseAndSpeed();
        }

        private void OnSpeedClicked()
        {
            _speed2x = !_speed2x;
            ApplyTimeScale();
            RefreshPauseAndSpeed();
        }

        private void OnSkillClicked()
        {
            CombatEntity selected = _controller != null ? _controller.SelectedAlly : null;
            selected?.Skills?.TryActivate();
            if (selected != null)
            {
                RefreshOperator(selected);
                RefreshSkill(selected, true);
            }
        }

        private void OnRetreatClicked()
        {
            CombatEntity selected = _controller != null ? _controller.SelectedAlly : null;
            if (selected != null && _controller.TryRetreat(selected))
            {
                _controller.SelectAlly(null);
                ClosePanels();
            }
        }

        private void OnSettingsClicked()
        {
            Debug.Log("[HUD] 设置按钮：等待接入设置面板。");
            SettingsClicked?.Invoke();
        }

        private void OnShowSkillRangeClicked()
        {
            CombatEntity selected = _controller != null ? _controller.SelectedAlly : null;
            Debug.Log("[HUD] 技能范围预览：接口已预留。");
            SkillRangeRequested?.Invoke(selected);
        }

        private void ApplyTimeScale()
        {
            Time.timeScale = _detailSlowActive
                ? 0.1f
                : (_paused ? 0f : (_speed2x ? 2f : 1f));
        }

        private void OnDestroy()
        {
            UnsubscribeController();
            UnbindAlly();
            Time.timeScale = 1f;
        }

        private void EnsurePanel<T>(GameObject panelObject) where T : PanelBase
        {
            if (panelObject == null || panelObject.GetComponent<T>() != null)
            {
                return;
            }

            panelObject.AddComponent<T>();
        }

        private static Image CreateCooldownMask(Image skillButtonImage)
        {
            if (skillButtonImage == null)
            {
                return null;
            }

            var maskObject = new GameObject(
                "SkillCooldownMask",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));

            RectTransform rect = maskObject.GetComponent<RectTransform>();
            rect.SetParent(skillButtonImage.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            Image image = maskObject.GetComponent<Image>();
            // Filled Image 没有 sprite 时不会按 fillAmount 裁剪，必须给一张白色底图。
            image.sprite = GetWhiteSprite();
            image.color = new Color(0.15f, 0.9f, 0.35f, 0.62f);
            image.raycastTarget = false;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Vertical;
            image.fillOrigin = (int)Image.OriginVertical.Bottom;
            image.fillClockwise = false;
            image.fillAmount = 1f;
            return image;
        }

        private static Sprite GetWhiteSprite()
        {
            return Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
        }

        private Text BindText(string path)
        {
            Text text = Find<Text>(path);
            if (text == null)
            {
                Debug.LogWarning("[HUD] 找不到 Text：" + path);
            }

            return text;
        }

        private Image BindImage(string path)
        {
            Image image = Find<Image>(path);
            if (image == null)
            {
                Debug.LogWarning("[HUD] 找不到 Image：" + path);
            }

            return image;
        }

        private GameObject FindObject(string path)
        {
            Transform target = transform.Find(path);
            if (target == null)
            {
                Debug.LogWarning("[HUD] 找不到对象：" + path);
            }

            return target != null ? target.gameObject : null;
        }

        private T Find<T>(string path) where T : Component
        {
            Transform target = transform.Find(path);
            return target != null ? target.GetComponent<T>() : null;
        }

        private static void HookButton(Button button, Action action)
        {
            if (button != null)
            {
                button.onClick.AddListener(() => action());
            }
        }

        private static void SetText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }

        private static Sprite LoadSprite(string name)
        {
            return UiAtlasService.Get(name, "UIHandDrawn/" + name);
        }

        /// <summary>
        /// 把 HUD 上所有已挂 Sprite 的 Image 换成图集版本：
        /// 场景里直接引用的图标不走 LoadSprite，不换的话它们仍按独立贴图分批。
        /// 图集里没有的（立绘、背景）保持原样。
        /// </summary>
        private void ApplyAtlasSprites()
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            int replaced = 0;
            int total = 0;
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image == null || image.sprite == null)
                {
                    continue;
                }

                total++;
                Sprite atlasSprite = UiAtlasService.Get(image.sprite.name, null);
                if (atlasSprite != null)
                {
                    image.sprite = atlasSprite;
                    replaced++;
                }
            }

            Debug.Log($"[UIAtlas] HUD 图标替换 {replaced}/{total} 个");
        }

        /// <summary>
        /// 头像/技能图标只加载一次：优先命中图集，图集缺失时回退单图并计数。
        /// </summary>
        private Sprite LoadSpriteCached(string resourcePath)
        {
            if (_spriteCache.TryGetValue(resourcePath, out Sprite cached))
            {
                return cached;
            }

            Sprite sprite = UiAtlasService.GetByPath(resourcePath);
            _spriteCache[resourcePath] = sprite;
            return sprite;
        }

        private static string GetSpTypeLabel(SpRecoveryType type)
        {
            switch (type)
            {
                case SpRecoveryType.Auto:
                    return "自动回复";
                case SpRecoveryType.Attack:
                    return "攻击回复";
                default:
                    return "受击回复";
            }
        }
    }
}
