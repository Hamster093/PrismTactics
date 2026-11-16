/****************************************************
    文件：OperatorRangeView.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/27 14:10:00
	功能：干员攻击范围地面预览，橙色半透明格子跟随干员朝向显示。
*****************************************************/

using System.Collections.Generic;
using PrismTactics.Combat;
using UnityEngine;

namespace PrismTactics.Battle
{
    public sealed class OperatorRangeView : MonoBehaviour
    {
        private GameObject _tileRoot;
        private readonly List<GameObject> _tiles = new List<GameObject>();
        private bool _visible;
        private bool _hasState;

        /// <summary>按范围格子从对象池租用贴片，并建立跟随本单位的根节点。</summary>
        public void Initialize(Vector2Int[] rangeCells, float groundLocalY, float parentScale)
        {
            ClearTiles();

            parentScale = Mathf.Max(0.01f, parentScale);
            _tileRoot = new GameObject("AttackRangeTiles");
            _tileRoot.transform.SetParent(transform, false);
            _tileRoot.transform.localPosition = new Vector3(0f, groundLocalY / parentScale, 0f);
            _tileRoot.transform.localScale = Vector3.one / parentScale;

            BattleTilePool pool = BattleTilePool.Instance;
            if (rangeCells == null || pool == null)
            {
                SetVisible(false);
                return;
            }

            for (int i = 0; i < rangeCells.Length; i++)
            {
                Vector2Int cell = rangeCells[i];
                // 地图地面块最高约 0.4，范围块必须略高于地面，否则会被地板遮住。
                Vector3 world = _tileRoot.transform.TransformPoint(new Vector3(cell.x, 0.44f, cell.y));
                GameObject tile = pool.Rent(
                    _tileRoot.transform,
                    world,
                    new Vector3(0.92f, 0.02f, 0.92f),
                    false);
                tile.name = "RangeTile_" + cell.x + "_" + cell.y;
                _tiles.Add(tile);
            }

            SetVisible(false);
        }

        /// <summary>切换范围块显隐，内部缓存状态避免重复调用引擎接口。</summary>
        public void SetVisible(bool visible)
        {
            if (_tileRoot == null)
            {
                return;
            }

            if (_hasState && _visible == visible)
            {
                return;
            }

            _visible = visible;
            _hasState = true;
            _tileRoot.SetActive(visible);
        }

        /// <summary>归还所有贴片并销毁根节点。</summary>
        private void ClearTiles()
        {
            BattleTilePool pool = BattleTilePool.Instance;
            if (pool != null)
            {
                pool.ReturnAll(_tiles);
            }
            else
            {
                for (int i = 0; i < _tiles.Count; i++)
                {
                    if (_tiles[i] != null)
                    {
                        BattleObjectUtility.Destroy(_tiles[i]);
                    }
                }
            }

            _tiles.Clear();

            if (_tileRoot != null)
            {
                BattleObjectUtility.Destroy(_tileRoot);
                _tileRoot = null;
            }
        }

        /// <summary>销毁时归还全部贴片，避免池对象被场景一起销毁。</summary>
        private void OnDestroy()
        {
            ClearTiles();
        }
    }
}
