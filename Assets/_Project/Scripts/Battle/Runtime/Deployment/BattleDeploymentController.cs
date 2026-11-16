/****************************************************
    文件：BattleDeploymentController.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/26 15:55:00
	功能：部署输入：选择干员、旋转朝向、点击格子部署。
*****************************************************/

using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class BattleDeploymentController : MonoBehaviour
    {
        private BattleController _controller;
        private BattleGrid _grid;
        private Camera _camera;
        private OperatorConfig _selected;
        private GridFacing _facing = GridFacing.Right;

        public OperatorConfig Selected => _selected;
        public GridFacing Facing => _facing;
        public bool InputEnabled { get; set; } = true;

        public void Initialize(BattleController controller, BattleGrid grid, Camera camera)
        {
            _controller = controller;
            _grid = grid;
            _camera = camera;
        }

        /// <summary>
        /// 给 UI 按钮调用：选择指定干员。
        /// </summary>
        public void SelectOperator(OperatorConfig operatorConfig)
        {
            _selected = operatorConfig;
            _facing = operatorConfig != null ? operatorConfig.defaultFacing : GridFacing.Right;
        }

        /// <summary>
        /// 给 UI 按钮调用：按索引选择干员。
        /// </summary>
        public void SelectOperator(int index)
        {
            if (_controller == null || _controller.Operators == null)
            {
                return;
            }

            if (index >= 0 && index < _controller.Operators.Count)
            {
                SelectOperator(_controller.Operators[index]);
            }
        }

        public void CancelSelection()
        {
            _selected = null;
        }

        public void SetFacing(GridFacing facing)
        {
            _facing = facing;
        }

        private void Update()
        {
            if (!InputEnabled || _controller == null || _controller.State != BattleState.Running)
            {
                return;
            }

            HandleSelection();
            HandleFacing();
            HandleDeployClick();
        }

        private void HandleSelection()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelSelection();
                return;
            }

            if (_controller.Operators == null)
            {
                return;
            }

            for (int i = 0; i < _controller.Operators.Count && i < 9; i++)
            {
                KeyCode key = (KeyCode)((int)KeyCode.Alpha1 + i);
                if (Input.GetKeyDown(key))
                {
                    SelectOperator(_controller.Operators[i]);
                }
            }
        }

        private void HandleFacing()
        {
            if (_selected == null || !Input.GetKeyDown(KeyCode.R))
            {
                return;
            }

            SetFacing(GridFacingUtility.RotateClockwise(_facing));
        }

        private void HandleDeployClick()
        {
            if (_selected == null || _camera == null || !Input.GetMouseButtonDown(0))
            {
                return;
            }

            // 顶部 HUD 区域不参与部署点击。
            if (Input.mousePosition.y > Screen.height - 110f)
            {
                return;
            }

            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float distance))
            {
                return;
            }

            Vector3 world = ray.GetPoint(distance);
            Vector2Int cell = _grid.Map.WorldToCell(world);
            if (_controller.TryDeploy(_selected, cell, _facing))
            {
                Debug.Log($"[部署] {_selected.displayName} -> ({cell.x},{cell.y})，朝向 {_facing}");
                CancelSelection();
            }
        }
    }
}
