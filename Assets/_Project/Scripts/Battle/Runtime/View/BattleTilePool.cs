/****************************************************
    文件：BattleTilePool.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/11/09 17:10:00
	功能：战斗格子片对象池，统一提供范围块、部署高亮和区域贴片，共享两套材质。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;

namespace PrismTactics.Battle
{
    /// <summary>
    /// 格子片对象池：一次预热，运行期只做租用/归还。
    /// 池挂在战斗场景根节点，随场景销毁自动回收。
    /// </summary>
    public sealed class BattleTilePool : MonoBehaviour
    {
        // 预热数量：覆盖最大地图的全部格子，避免运行期扩容
        private const int PrewarmCount = 128;

        // 池实例，供 OperatorRangeView / 拖拽控制器直接取用
        public static BattleTilePool Instance { get; private set; }

        private readonly Stack<GameObject> _idle = new Stack<GameObject>();
        private Transform _root;
        private Material _rangeMaterial;
        private Material _highlightMaterial;

        /// <summary>在指定父节点下创建池并完成预热，重复调用返回已有实例。</summary>
        public static BattleTilePool Create(Transform parent)
        {
            if (Instance != null)
            {
                return Instance;
            }

            GameObject poolObject = new GameObject("Battle Tiles");
            poolObject.transform.SetParent(parent, false);
            Instance = poolObject.AddComponent<BattleTilePool>();
            Instance.Initialize();
            return Instance;
        }

        /// <summary>创建根节点、两套共享材质并预热 128 个格子片。</summary>
        private void Initialize()
        {
            _root = transform;
            _rangeMaterial = CreateTransparentMaterial(new Color(1f, 0.48f, 0.08f, 0.46f));
            _highlightMaterial = CreateTransparentMaterial(new Color(0.15f, 1f, 0.35f, 0.35f));

            for (int i = 0; i < PrewarmCount; i++)
            {
                GameObject tile = CreateTile();
                tile.SetActive(false);
                _idle.Push(tile);
            }
        }

        /// <summary>租用一个格子片：设置父节点、世界坐标、局部缩放与材质后激活。</summary>
        public GameObject Rent(Transform parent, Vector3 worldPosition, Vector3 localScale, bool highlight)
        {
            GameObject tile = _idle.Count > 0 ? _idle.Pop() : CreateTile();
            tile.transform.SetParent(parent, false);
            tile.transform.position = worldPosition;
            tile.transform.localScale = localScale;

            Renderer renderer = tile.GetComponent<Renderer>();
            Material material = highlight ? _highlightMaterial : _rangeMaterial;
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            tile.SetActive(true);
            return tile;
        }

        /// <summary>归还一个格子片：隐藏并挂回池根节点。</summary>
        public void Return(GameObject tile)
        {
            if (tile == null)
            {
                return;
            }

            tile.SetActive(false);
            tile.transform.SetParent(_root, false);
            _idle.Push(tile);
        }

        /// <summary>批量归还，调用方自己清空列表。</summary>
        public void ReturnAll(List<GameObject> tiles)
        {
            if (tiles == null)
            {
                return;
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                Return(tiles[i]);
            }
        }

        /// <summary>创建一个无碰撞、不投影的格子片，默认挂在池根节点下。</summary>
        private GameObject CreateTile()
        {
            GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "BattleTile";
            tile.transform.SetParent(_root, false);

            Collider collider = tile.GetComponent<Collider>();
            if (collider != null)
            {
                BattleObjectUtility.Destroy(collider);
            }

            Renderer renderer = tile.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return tile;
        }

        /// <summary>创建一套半透明共享材质；Standard 被裁剪时回退到 Sprites/Default。</summary>
        private static Material CreateTransparentMaterial(Color color)
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

            if (shader == null)
            {
                Debug.LogError("[BattleTilePool] 找不到可用 Shader，格子片材质将跳过。");
                return null;
            }

            var material = new Material(shader);
            material.color = color;
            material.renderQueue = 3000;
            if (shader.name == "Standard")
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
            }

            return material;
        }

        /// <summary>场景卸载时清空静态引用并销毁共享材质。</summary>
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_rangeMaterial != null)
            {
                Destroy(_rangeMaterial);
            }

            if (_highlightMaterial != null)
            {
                Destroy(_highlightMaterial);
            }
        }
    }
}
