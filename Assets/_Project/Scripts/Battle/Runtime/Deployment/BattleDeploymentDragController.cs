/****************************************************
    文件：BattleDeploymentDragController.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 12:30:00
	功能：卡牌拖拽部署：0.1 倍速、合法格高亮、方向选择、取消与恢复时间。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 卡牌拖拽部署控制器：把"从 HUD 卡牌拖到战场格子"的交互串成一个状态机。
    ///
    /// 状态流转：
    ///   Idle ──(BeginCardDrag)──► DraggingCard ──(EndCardDrag 且格子合法)──► ChoosingDirection
    ///     ▲                                                                        │
    ///     └──────────────────(PlaceOperator / CancelDeployment)───────────────────┘
    ///
    /// 职责边界：
    /// - 本类只管"交互过程"：跟随鼠标的 ghost、合法格高亮、方向拖拽、预览面板；
    /// - 真正的部署由 BattleController.TryDeploy 完成，本类不直接创建干员。
    /// </summary>
    public sealed class BattleDeploymentDragController : MonoBehaviour
    {
        #region 状态枚举
        /// <summary>
        /// 拖拽部署的三个阶段：
        /// - Idle：空闲，可开始新的拖拽；
        /// - DraggingCard：卡牌已拿起，ghost 跟随鼠标，合法格高亮显示；
        /// - ChoosingDirection：已落在合法格上，进入方向选择（在 ActionPanel 内拖拽决定朝向）。
        /// </summary>
        private enum DragState
        {
            Idle,
            DraggingCard,
            DraggingItem,
            ChoosingDirection
        }
        #endregion
        #region 依赖与状态字段
        // 外部注入的依赖：战斗总控、格子、相机、HUD、普通部署控制器
        private BattleController _controller;
        private BattleGrid _grid;
        private Camera _camera;
        private BattleHudBinder _hud;
        private BattleDeploymentController _deployment;
        // 当前正在拖拽的干员配置
        private OperatorConfig _operator;
        // 当前正在拖拽的道具配置（与 _operator 互斥）
        private ItemDefinition _item;
        private DragState _state;// 当前状态机状态
        private Transform _queue;// HUD 中卡牌队列的根节点（DeployQueuePanel）
        #endregion

        #region Ghost 与高亮视觉字段
        // 跟随鼠标的半透明预览体（ghost），以及它的渲染器与范围视图
        private GameObject _ghost;
        private Renderer _ghostRenderer;
        private Material _ghostMaterial;
        private OperatorRangeView _ghostRangeView;
        // ghost 的基础颜色（来自干员 debugColor），合法/非法时在此基础上调整
        private Color _ghostBaseColor = Color.white;
        // 合法格高亮的根节点与所有高亮方块
        private GameObject _highlightRoot;
        private readonly List<GameObject> _highlights = new List<GameObject>();
        #endregion

        #region 格子与方向字段

        // 当前鼠标对应的格子、是否命中格子、格子是否可部署
        private Vector2Int _cell;
        private bool _hasCell;
        private bool _validCell;
        // 当前朝向（方向选择阶段由拖拽方向决定）
        private GridFacing _facing = GridFacing.Left;
        // ActionPanel 的 RectTransform，用于判断鼠标是否在面板内
        private RectTransform _actionPanelRect;
        // 方向拖拽的运行时状态：是否正在拖、起始屏幕坐标
        private bool _directionDragging;
        private Vector2 _directionStart;
        #endregion

        #region 初始化与生命周期

        /// <summary>
        /// 由 M1BattleBootstrap 调用：注入依赖并绑定 HUD 卡牌。
        /// 订阅 AlliesChanged 以便干员部署/撤退后刷新卡牌显隐。
        /// </summary>
        public void Initialize(
            BattleController controller,
            BattleGrid grid,
            Camera camera,
            BattleHudBinder hud,
            BattleDeploymentController deployment)
        {
            _controller = controller;
            _grid = grid;
            _camera = camera;
            _hud = hud;
            _deployment = deployment;
            // 缓存 ActionPanel 的 RectTransform；HUD 缺失时为 null
            _actionPanelRect = hud != null ? hud.ActionPanelRect : null;
            // 干员列表变化时刷新卡牌（已部署的卡隐藏）
            if (_controller != null)
            {
                _controller.AlliesChanged += RefreshCards;
                _controller.ItemsChanged += RefreshCards;
            }

            BindCards();
        }
        /// <summary>
        /// 销毁时反订阅 AlliesChanged / ItemsChanged，避免控制器被销毁后事件仍指向本对象。
        /// </summary>
        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.AlliesChanged -= RefreshCards;
                _controller.ItemsChanged -= RefreshCards;
            }
        }
        #endregion

        #region 公开接口：点击与拖拽生命周期
        /// <summary>
        /// 点击卡牌：显示干员的预备详情。
        /// 仅在 Idle 状态、干员未部署时响应，避免拖拽中途或已部署时弹出。
        /// </summary>
        public void ShowReserveDetail(OperatorConfig operatorConfig)
        {
            if (_state != DragState.Idle
                || operatorConfig == null
                || _controller == null
                || _controller.IsOperatorDeployed(operatorConfig))
            {
                return;
            }

            _hud?.ShowOperatorPreview(operatorConfig);
        }

        /// <summary>
        /// 开始拖拽卡牌：
        /// 1. 校验状态、干员与可部署性；
        /// 2. 关闭普通部署控制器与 HUD 的输入，避免冲突；
        /// 3. 创建 ghost 与合法格高亮；
        /// 4. 切到 DraggingCard 状态。
        /// </summary>
        public void BeginCardDrag(OperatorConfig operatorConfig)
        {
            if (_state != DragState.Idle
                || operatorConfig == null
                || _controller == null
                || !_controller.CanDeploy(operatorConfig))
            {
                return;
            }

            _operator = operatorConfig;
            _facing = GridFacing.Left;// 每次拖拽重置朝向
            _state = DragState.DraggingCard;

            // 拖拽期间禁用其他部署输入，避免与普通部署控制器抢输入
            if (_deployment != null)
            {
                _deployment.CancelSelection();
                _deployment.InputEnabled = false;
            }

            if (_hud != null)
            {
                _hud.InputEnabled = false;
                _hud.ShowOperatorPreview(operatorConfig);
                _hud.SetSkillRoomVisible(false);
            }

            CreateGhost(operatorConfig);
            CreateHighlights(operatorConfig);
        }
        /// <summary>
        /// 开始拖动道具卡：校验次数与状态，创建道具 ghost 和空闲格高亮。
        /// 道具没有方向选择阶段，松手即生效。
        /// </summary>
        public void BeginItemDrag(ItemDefinition item)
        {
            if (_state != DragState.Idle
                || item == null
                || _controller == null
                || !_controller.CanUseItem(item))
            {
                return;
            }

            _item = item;
            _state = DragState.DraggingItem;

            if (_deployment != null)
            {
                _deployment.CancelSelection();
                _deployment.InputEnabled = false;
            }

            if (_hud != null)
            {
                _hud.InputEnabled = false;
                _hud.BeginItemPreview();
            }

            CreateItemGhost(item);
            CreateItemHighlights();
        }
        /// <summary>
        /// 拖拽中：把屏幕坐标转成世界坐标 → 格子坐标，更新 ghost 位置与颜色。
        /// 可部署时 ghost 吸附到格子，不可部署时自由跟随鼠标。
        /// </summary>
        public void DragCard(Vector2 screenPosition)
        {
            if (_state != DragState.DraggingCard && _state != DragState.DraggingItem)
            {
                return;
            }

            if (!TryGetWorld(screenPosition, out Vector3 world))
            {
                _hasCell = false;
                _validCell = false;
                SetGhostActive(false);
                return;
            }
            // 世界坐标 → 格子坐标，并判定是否在界内、是否可放置
            _cell = _grid.Map.WorldToCell(world);
            _hasCell = _grid.Map.IsInside(_cell);
            _validCell = _hasCell && (_item != null
                ? _grid.IsItemPlaceable(_cell)
                : _grid.IsDeployable(
                    _cell,
                    _operator.canDeployOnHighGround,
                    _operator.requiresHighGround));

            SetGhostActive(true);

            // 可放置时吸附格子；不可放置时保持自由跟随鼠标，避免“到处粘格子”。
            _ghost.transform.position = _validCell
                ? _grid.Map.CellToWorld(_cell) + Vector3.up * (_item != null ? 0.35f : 0.55f)
                : world + Vector3.up * (_item != null ? 0.35f : 0.55f);
            // 合法为半透明基色，非法为红色
            if (_ghostRenderer != null && _ghostMaterial != null)
            {
                _ghostMaterial.color = _validCell
                    ? new Color(_ghostBaseColor.r, _ghostBaseColor.g, _ghostBaseColor.b, 0.58f)
                    : new Color(0.95f, 0.18f, 0.12f, 0.5f);
            }
        }
        /// <summary>
        /// 结束拖拽：再做一次 DragCard 确认最终落点。
        /// - 落点非法 → 取消部署；
        /// - 落点合法 → 进入 ChoosingDirection，把 ActionPanel 移到鼠标位置等待方向拖拽。
        /// </summary>
        public void EndCardDrag(Vector2 screenPosition)
        {
            if (_state != DragState.DraggingCard && _state != DragState.DraggingItem)
            {
                return;
            }
            // 用最终位置再更新一次，避免最后一帧没跟上
            DragCard(screenPosition);
            if (!_hasCell || !_validCell)
            {
                CancelDeployment();
                return;
            }

            // 道具松手即生效，没有方向选择阶段
            if (_item != null)
            {
                PlaceItem();
                return;
            }

            _state = DragState.ChoosingDirection;//切换选择方向状态
            _directionDragging = false;
            _hud?.SetSkillRoomVisible(false);
            _hud?.ShowActionPanelAt(screenPosition);
        }
        #endregion

        #region 方向选择（Update 驱动）
        /// <summary>
        /// 仅在 ChoosingDirection 状态下运行：
        /// - 左键按下且不在 ActionPanel 内 → 取消；
        /// - 在面板内按下 → 开始方向拖拽；
        /// - 拖动超过阈值（10 像素）→ 更新朝向；
        /// - 松开 → 落子。
        /// 只要求在面板内按下开始拖拽；松开可以移出面板，用拖动方向决定朝向。
        /// </summary>
        private void Update()
        {
            if (_state != DragState.ChoosingDirection || _actionPanelRect == null)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                // 在面板外按下视为取消
                if (!IsInsideActionPanel(Input.mousePosition))
                {
                    CancelDeployment();
                    return;
                }

                _directionDragging = true;
                _directionStart = Input.mousePosition;
            }

            if (!_directionDragging)
            {
                return;
            }

            if (Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - _directionStart;
                // 阈值 100 = 10 像素²，避免手抖误触改变朝向
                if (delta.sqrMagnitude >= 100f)
                {
                    _facing = GetFacing(delta);
                    SetGhostFacing();
                    UpdateGhostColor(true);
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                // 只要求在面板内按下开始拖拽；松开可以移出面板，用拖动方向决定朝向。
                _directionDragging = false;
                PlaceOperator();
            }
        }
        /// <summary>
        /// 最终落子：调用 BattleController.TryDeploy。
        /// 成功则清理并取消选中；失败（理论上不该发生，因为前面已校验）则取消部署。
        /// </summary>
        private void PlaceOperator()
        {
            if (!_hasCell || !_validCell || _operator == null)
            {
                CancelDeployment();
                return;
            }

            bool placed = _controller.TryDeploy(_operator, _cell, _facing);
            if (!placed)
            {
                CancelDeployment();
                return;
            }

            Cleanup();
            _controller.SelectAlly(null);
            _hud?.ClearPreview();
        }
        /// <summary>
        /// 最终放置道具：调用 BattleController.TryUseItem，成功或失败都回到 Idle。
        /// </summary>
        private void PlaceItem()
        {
            if (!_hasCell || !_validCell || _item == null)
            {
                CancelDeployment();
                return;
            }

            _controller.TryUseItem(_item, _cell);
            Cleanup();
            _hud?.ClearPreview();
        }
        /// <summary>
        /// 取消部署：等价于 Cleanup + 清除 HUD 预览。
        /// 任何非法路径（落点非法、面板外点击、部署失败）都走这里。
        /// </summary>
        private void CancelDeployment()
        {
            Cleanup();
            _hud?.ClearPreview();
        }
        /// <summary>
        /// 统一清理：重置状态与运行时字段、恢复其他输入、销毁 ghost 与高亮。
        /// 必须保证幂等（重复调用不报错），因为成功和取消路径都会走它。
        /// </summary>
        private void Cleanup()
        {
            _state = DragState.Idle;
            _operator = null;
            _item = null;
            _directionDragging = false;
            _hasCell = false;
            _validCell = false;
            // 恢复普通部署控制器与 HUD 的输入
            if (_deployment != null)
            {
                _deployment.InputEnabled = true;
            }

            if (_hud != null)
            {
                _hud.InputEnabled = true;
            }
            // 销毁 ghost
            if (_ghost != null)
            {
                BattleObjectUtility.Destroy(_ghost);
                _ghost = null;
                _ghostRenderer = null;
                _ghostRangeView = null;
            }

            if (_ghostMaterial != null)
            {
                BattleObjectUtility.Destroy(_ghostMaterial);
                _ghostMaterial = null;
            }

            // 归还所有高亮方块
            BattleTilePool pool = BattleTilePool.Instance;
            if (pool != null)
            {
                pool.ReturnAll(_highlights);
            }

            _highlights.Clear();
            // 销毁高亮根节点
            if (_highlightRoot != null)
            {
                BattleObjectUtility.Destroy(_highlightRoot);
                _highlightRoot = null;
            }
        }
        #endregion

        #region 卡牌绑定与刷新
        /// <summary>
        /// 绑定 HUD 卡牌队列：
        /// 1. 找到 HUD/DeployQueuePanel；
        /// 2. 遍历 DeploySlot1..9，按 Operators 数量决定显示与否；
        /// 3. 加载干员头像，挂载 DeployCardDragHandler 并注入本控制器；
        /// 4. 最后刷新一次显隐（隐藏已部署的卡）。
        /// </summary>
        private void BindCards()
        {
            if (_controller == null || _controller.Operators == null)
            {
                return;
            }

            GameObject hudObject = GameObject.Find("HUD");
            Transform queue = hudObject != null ? hudObject.transform.Find("DeployQueuePanel") : null;
            if (queue == null)
            {
                return;
            }

            _queue = queue;
            int slotIndex = 0;
            // 干员卡优先占前面的槽位
            for (int i = 0; i < _controller.Operators.Count && slotIndex < 9; i++, slotIndex++)
            {
                Transform slot = queue.Find("DeploySlot" + (slotIndex + 1));
                if (slot == null)
                {
                    continue;
                }

                slot.gameObject.SetActive(true);
                OperatorConfig operatorConfig = _controller.Operators[i];
                // 设置卡面：优先裁切头像，缺失时回退原图
                Image image = slot.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = OperatorPortraitUtility.LoadCropped(operatorConfig.operatorId);
                    if (image.sprite == null)
                    {
                        image.sprite = OperatorPortraitUtility.Load(operatorConfig.operatorId);
                    }

                    image.color = Color.white;
                    image.preserveAspect = true;
                    image.raycastTarget = true; // 打开，确保收不到拖拽事件
                }
                // 挂载并初始化拖拽转发器
                DeployCardDragHandler handler = slot.GetComponent<DeployCardDragHandler>();
                if (handler == null)
                {
                    handler = slot.gameObject.AddComponent<DeployCardDragHandler>();
                }

                handler.Initialize(this, operatorConfig);
            }
            // 道具卡接在干员卡后面
            if (_controller.Items != null)
            {
                for (int i = 0; i < _controller.Items.Count && slotIndex < 9; i++, slotIndex++)
                {
                    Transform slot = queue.Find("DeploySlot" + (slotIndex + 1));
                    if (slot == null)
                    {
                        continue;
                    }

                    slot.gameObject.SetActive(true);
                    BindItemCard(slot, _controller.Items[i]);
                }
            }
            // 多余槽位隐藏
            for (; slotIndex < 9; slotIndex++)
            {
                Transform slot = queue.Find("DeploySlot" + (slotIndex + 1));
                if (slot != null)
                {
                    slot.gameObject.SetActive(false);
                }
            }

            RefreshCards();
        }
        /// <summary>
        /// 把道具配置绑到卡槽：底色、拖拽转发器和名称/次数文字。
        /// </summary>
        private void BindItemCard(Transform slot, ItemDefinition item)
        {
            Image image = slot.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = null;
                image.color = new Color(item.colorR, item.colorG, item.colorB, 0.9f);
                image.raycastTarget = true;
            }

            ItemCardDragHandler handler = slot.GetComponent<ItemCardDragHandler>();
            if (handler == null)
            {
                handler = slot.gameObject.AddComponent<ItemCardDragHandler>();
            }

            handler.Initialize(this, item);

            Transform labelTransform = slot.Find("ItemLabel");
            Text label = labelTransform != null ? labelTransform.GetComponent<Text>() : null;
            if (label == null)
            {
                GameObject labelObject = new GameObject("ItemLabel", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(slot, false);
                RectTransform rect = labelObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                label = labelObject.GetComponent<Text>();
                label.alignment = TextAnchor.MiddleCenter;
                label.fontSize = 14;
                label.color = Color.white;
                label.font = LoadDefaultFont();
            }

            label.text = item.displayName + "\n×" + _controller.GetItemCharges(item);
        }
        /// <summary>加载内置字体，优先 LegacyRuntime。</summary>
        private static Font LoadDefaultFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font != null ? font : Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
        /// <summary>
        /// 刷新卡牌显隐：干员已部署则隐藏；道具次数为 0 则隐藏，并更新剩余次数文字。
        /// 由 AlliesChanged / ItemsChanged 事件驱动。
        /// </summary>
        private void RefreshCards()
        {
            if (_queue == null || _controller == null || _controller.Operators == null)
            {
                return;
            }

            int slotIndex = 0;
            for (int i = 0; i < _controller.Operators.Count && slotIndex < 9; i++, slotIndex++)
            {
                Transform slot = _queue.Find("DeploySlot" + (slotIndex + 1));
                if (slot == null)
                {
                    continue;
                }

                slot.gameObject.SetActive(!_controller.IsOperatorDeployed(_controller.Operators[i]));
            }

            if (_controller.Items != null)
            {
                for (int i = 0; i < _controller.Items.Count && slotIndex < 9; i++, slotIndex++)
                {
                    Transform slot = _queue.Find("DeploySlot" + (slotIndex + 1));
                    if (slot == null)
                    {
                        continue;
                    }

                    ItemDefinition item = _controller.Items[i];
                    int charges = _controller.GetItemCharges(item);
                    slot.gameObject.SetActive(charges > 0);

                    Transform labelTransform = slot.Find("ItemLabel");
                    Text label = labelTransform != null ? labelTransform.GetComponent<Text>() : null;
                    if (label != null)
                    {
                        label.text = item.displayName + "\n×" + charges;
                    }
                }
            }
        }
        #endregion

        #region Ghost 视觉

        /// <summary>
        /// 创建 ghost：半透明立方体 + 范围视图。
        /// 材质用 Standard + Transparent 混合参数手工配置（内置管线标准做法）。
        /// 初始隐藏，等 DragCard 第一次拿到世界坐标后再显示。
        /// </summary>
        private void CreateGhost(OperatorConfig operatorConfig)
        {
            _ghostBaseColor = operatorConfig.debugColor;

            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "DeployGhost";
            _ghost.transform.localScale = Vector3.one * 0.7f;
            _ghostRenderer = _ghost.GetComponent<Renderer>();
            // 半透明材质：Standard + Transparent 混合
            _ghostMaterial = CreateTransparentMaterial(
                new Color(_ghostBaseColor.r, _ghostBaseColor.g, _ghostBaseColor.b, 0.58f));
            _ghostRenderer.sharedMaterial = _ghostMaterial;
            // 移除碰撞体，避免 ghost 挡住射线/触发物理
            Collider collider = _ghost.GetComponent<Collider>();
            if (collider != null)
            {
                BattleObjectUtility.Destroy(collider);
            }
            // 范围视图：与干员同款，但向下偏移 0.55 贴合 ghost 位置
            _ghostRangeView = _ghost.AddComponent<OperatorRangeView>();
            _ghostRangeView.Initialize(operatorConfig.rangeCells, -0.55f, 0.7f);
            _ghostRangeView.SetVisible(true);
            SetGhostFacing();
            SetGhostActive(false);
        }
        /// <summary>
        /// 创建道具 ghost：半透明小方块 + 作用范围预览（与干员攻击范围同款）。
        /// </summary>
        private void CreateItemGhost(ItemDefinition item)
        {
            _ghostBaseColor = new Color(item.colorR, item.colorG, item.colorB);

            _ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ghost.name = "ItemGhost";
            _ghost.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            _ghostRenderer = _ghost.GetComponent<Renderer>();
            _ghostMaterial = CreateTransparentMaterial(
                new Color(_ghostBaseColor.r, _ghostBaseColor.g, _ghostBaseColor.b, 0.58f));
            _ghostRenderer.sharedMaterial = _ghostMaterial;

            Collider collider = _ghost.GetComponent<Collider>();
            if (collider != null)
            {
                BattleObjectUtility.Destroy(collider);
            }

            // 作用范围预览：半径 1 即 3x3，向下偏移贴合 ghost 位置
            _ghostRangeView = _ghost.AddComponent<OperatorRangeView>();
            _ghostRangeView.Initialize(BuildSquareRange(item.radius), -0.35f, 0.5f);
            _ghostRangeView.SetVisible(true);

            SetGhostActive(false);
        }
        /// <summary>把作用半径展开成方形范围格子，中心为 (0,0)，与干员范围配置同格式。</summary>
        private static Vector2Int[] BuildSquareRange(int radius)
        {
            radius = Mathf.Max(0, radius);
            int size = radius * 2 + 1;
            var cells = new List<Vector2Int>(size * size);
            for (int x = -radius; x <= radius; x++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    cells.Add(new Vector2Int(x, y));
                }
            }

            return cells.ToArray();
        }
        /// <summary>
        /// 创建一套 Standard 半透明材质，供 ghost 使用。
        /// </summary>
        private static Material CreateTransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            var material = new Material(shader);
            material.color = color;
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = 3000;
            return material;
        }
        /// <summary>
        /// 根据 _facing 更新 ghost 的 Y 轴旋转。
        /// </summary>
        private void SetGhostFacing()
        {
            if (_ghost != null)
            {
                _ghost.transform.rotation = Quaternion.Euler(0f, GetFacingYaw(_facing), 0f);
            }
        }
        /// <summary>
        /// 网格朝向 → Y 轴旋转角。默认 0 对应"右"，是 2D 素材的常见默认朝向。
        /// （与 BattleController.GetFacingYaw 逻辑相同，各自私有，未提取公共工具。）
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
        /// <summary>
        /// 切换 ghost 显隐（鼠标未命中平面时隐藏）。
        /// </summary>
        private void SetGhostActive(bool active)
        {
            if (_ghost != null)
            {
                _ghost.SetActive(active);
            }
        }
        /// <summary>
        /// 更新 ghost 颜色：合法为半透明基色，非法为红色。
        /// 与 DragCard 内的颜色逻辑一致，供方向选择阶段复用。
        /// </summary>
        private void UpdateGhostColor(bool valid)
        {
            if (_ghostMaterial != null)
            {
                _ghostMaterial.color = valid
                    ? new Color(_ghostBaseColor.r, _ghostBaseColor.g, _ghostBaseColor.b, 0.58f)
                    : new Color(0.95f, 0.18f, 0.12f, 0.5f);
            }
        } 
        #endregion

        #region 合法格高亮

        /// <summary>
        /// 为所有可部署格生成绿色半透明高亮方块。
        /// 遍历整张地图，逐格调用 IsDeployable 筛选。
        /// 高度按干员是否要求高台区分：要求高台时浮得更高（0.46），否则贴地（0.12）。
        /// </summary>

        private void CreateHighlights(OperatorConfig operatorConfig)
        {
            _highlightRoot = new GameObject("Deployment Highlights");
            BattleTilePool pool = BattleTilePool.Instance;
            if (pool == null)
            {
                return;
            }

            for (int x = 0; x < _grid.Map.Width; x++)
            {
                for (int y = 0; y < _grid.Map.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!_grid.IsDeployable(
                            cell,
                            operatorConfig.canDeployOnHighGround,
                            operatorConfig.requiresHighGround))
                    {
                        continue;
                    }

                    Vector3 world = _grid.Map.CellToWorld(cell)
                                    + Vector3.up * (operatorConfig.requiresHighGround ? 0.46f : 0.12f);
                    GameObject cube = pool.Rent(
                        _highlightRoot.transform,
                        world,
                        new Vector3(0.92f, 0.04f, 0.92f),
                        true);
                    cube.name = "Highlight_" + x + "_" + y;
                    _highlights.Add(cube);
                }
            }
        }
        /// <summary>
        /// 道具放置高亮：遍历地图，把所有空闲格从对象池租出来显示。
        /// </summary>
        private void CreateItemHighlights()
        {
            _highlightRoot = new GameObject("Item Highlights");
            BattleTilePool pool = BattleTilePool.Instance;
            if (pool == null)
            {
                return;
            }

            for (int x = 0; x < _grid.Map.Width; x++)
            {
                for (int y = 0; y < _grid.Map.Height; y++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!_grid.IsItemPlaceable(cell))
                    {
                        continue;
                    }

                    Vector3 world = _grid.Map.CellToWorld(cell) + Vector3.up * 0.12f;
                    GameObject cube = pool.Rent(
                        _highlightRoot.transform,
                        world,
                        new Vector3(0.92f, 0.04f, 0.92f),
                        true);
                    cube.name = "Highlight_" + x + "_" + y;
                    _highlights.Add(cube);
                }
            }
        }
        #endregion

        #region 工具方法

        /// <summary>
        /// 屏幕坐标 → 世界坐标：用射线与 y=0 水平面求交。
        /// 相机缺失或射线与平面平行时返回 false。
        /// </summary>
        private bool TryGetWorld(Vector2 screenPosition, out Vector3 world)
        {
            world = default;
            if (_camera == null)
            {
                return false;
            }

            Ray ray = _camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float distance))
            {
                return false;
            }

            world = ray.GetPoint(distance);
            return true;
        }
        /// <summary>
        /// 判断屏幕点是否落在 ActionPanel 矩形内。
        /// 正确处理 ScreenSpaceOverlay（无需相机）和其他渲染模式（需要 worldCamera）。
        /// </summary>
        private bool IsInsideActionPanel(Vector2 screenPosition)
        {
            if (_actionPanelRect == null)
            {
                return false;
            }

            Canvas canvas = _actionPanelRect.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.RectangleContainsScreenPoint(
                _actionPanelRect,
                screenPosition,
                uiCamera);
        }
        /// <summary>
        /// 把拖拽向量转换为四方向朝向：水平分量大于垂直分量取左右，否则取上下。
        /// </summary>
        private static GridFacing GetFacing(Vector2 delta)
        {
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return delta.x >= 0f ? GridFacing.Right : GridFacing.Left;
            }

            return delta.y >= 0f ? GridFacing.Up : GridFacing.Down;
        }
        #endregion
    }
}
