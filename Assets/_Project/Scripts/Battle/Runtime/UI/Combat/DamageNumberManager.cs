/****************************************************
    文件：DamageNumberManager.cs
	作者：DADI
    邮箱: [email removed]
    日期：2026/10/29 15:30:00
	功能：屏幕空间伤害数字池，负责世界坐标转屏幕、上限回收和战斗结束清理。
*****************************************************/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PrismTactics.Battle
{
    public sealed class DamageNumberManager : MonoBehaviour
    {
        [SerializeField] private DamageNumberView prefab;
        [SerializeField] private Canvas screenCanvas;
        [SerializeField] private int maxActive = 50;
        [SerializeField] private float duration = 0.8f;
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0.75f, 0f);

        private readonly Stack<DamageNumberView> _pool = new Stack<DamageNumberView>();
        private readonly List<DamageNumberView> _active = new List<DamageNumberView>();
        private static DamageNumberManager _instance;

        public static DamageNumberManager Ensure()
        {
            if (_instance != null)
            {
                return _instance;
            }

            var canvasObject = new GameObject(
                "Damage Number Canvas",
                typeof(Canvas),
                typeof(CanvasScaler));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _instance = canvasObject.AddComponent<DamageNumberManager>();
            _instance.screenCanvas = canvas;
            _instance.prefab = AssetService.LoadComponent<DamageNumberView>("DamageNumber");
            if (_instance.prefab == null && Application.isPlaying)
            {
                Debug.LogError("[Asset] DamageNumber 预制体加载失败，或根节点没有 DamageNumberView。");
            }
            return _instance;
        }

        /// <summary>
        /// 战斗开始前预创建飘字对象，避免第一次密集受击时集中 Instantiate。
        /// 只创建、不激活，数量不超过 maxActive。
        /// </summary>
        public void Warmup(int count)
        {
            if (prefab == null || screenCanvas == null)
            {
                return;
            }

            PrunePool();
            int target = Mathf.Clamp(count, 0, Mathf.Max(1, maxActive));
            while (_pool.Count < target)
            {
                DamageNumberView view = Instantiate(prefab, screenCanvas.transform);
                view.gameObject.SetActive(false);
                _pool.Push(view);
            }
        }

        private void PrunePool()
        {
            if (_pool.Count == 0)
            {
                return;
            }

            var alive = new List<DamageNumberView>();
            while (_pool.Count > 0)
            {
                DamageNumberView view = _pool.Pop();
                if (view != null)
                {
                    alive.Add(view);
                }
            }

            for (int i = alive.Count - 1; i >= 0; i--)
            {
                _pool.Push(alive[i]);
            }
        }

        public void Spawn(Vector3 worldPosition, string content, Color color)
        {
            if (screenCanvas == null || prefab == null)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(worldPosition + worldOffset);
            if (screen.z < 0f
                || screen.x < 0f
                || screen.y < 0f
                || screen.x > Screen.width
                || screen.y > Screen.height)
            {
                return;
            }

            if (_active.Count >= Mathf.Max(1, maxActive))
            {
                Recycle(_active[0]);
            }

            DamageNumberView view = _pool.Count > 0
                ? _pool.Pop()
                : Instantiate(prefab, screenCanvas.transform);
            view.transform.SetParent(screenCanvas.transform, false);
            view.gameObject.SetActive(true);
            view.transform.SetAsLastSibling();
            view.OnFinished += HandleFinished;
            _active.Add(view);
            view.Play(new Vector2(screen.x, screen.y), content, color, duration);
        }

        public void Clear()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Recycle(_active[i]);
            }

            while (_pool.Count > 0)
            {
                Destroy(_pool.Pop().gameObject);
            }
        }

        /// <summary>
        /// 战斗结束/切面板时清空所有飘字，避免盖在结束面板上。
        /// </summary>
        public static void ClearAll()
        {
            if (_instance != null)
            {
                _instance.Clear();
            }
        }

        private void HandleFinished(DamageNumberView view)
        {
            if (view != null)
            {
                Recycle(view);
            }
        }

        private void Recycle(DamageNumberView view)
        {
            if (view == null)
            {
                return;
            }

            view.OnFinished -= HandleFinished;
            view.Stop();
            view.gameObject.SetActive(false);
            _active.Remove(view);
            _pool.Push(view);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
